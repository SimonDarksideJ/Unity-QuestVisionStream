# 2026-07 — Library hardening pass (`com.questvisionstream`)

**Scope:** the reusable TypeScript streaming library only
(`V2/com.questvisionstream/`) — the most reusable part of V2, consumed by the
IWSDK quest-client and any future host.
**Driver:** the findings in
[`../Architecture-Review-2026-07.md`](../Architecture-Review-2026-07.md)
(library bugs L1–L4) plus the validation/math/qualifier weaknesses and the
missing recovery story.
**Method:** red → green → measure, per the [strategy guide](README.md), same
as the [server pass](2026-07-Server-Hardening.md).

## Test infrastructure (new — the library had none)

The library targets browser APIs that don't exist in Node, so the new vitest
suite stubs them **at the global seam**: `MockWebSocket` and
`MockRTCPeerConnection` (`tests/helpers.ts`) are drop-in globals whose every
transition — open, error, late close, deferred `setRemoteDescription`,
candidate application — is scripted by the test. That is precisely what makes
the race-condition bugs below *deterministically* reproducible; against a real
network they are timing-dependent and flaky by nature.

Key properties:
- `MockRTCPeerConnection.addIceCandidate` **throws when the remote description
  is null**, mirroring the browser — the mock enforces the platform contract
  the bug violates.
- Unhandled promise rejections are asserted **explicitly**: tests attach their
  own `process.on('unhandledRejection')` collector and expect zero events
  (vitest's implicit fail-on-unhandled is disabled so red runs are
  deterministic, not worker-crash noise).
- Services are constructed directly with a minimal
  `ServiceActivationContext` stub — no `ServiceManager` needed, keeping tests
  at the unit seam.

## The red run (before any fix)

```
15 failed | 21 passed (36 tests, ~730ms)
```

The 21 passes are deliberate **baseline locks** on behaviour the review
verified correct: the coordinate math, the dedup policies, the ICE
`candidate:` prefix handling, channel routing, and reconnect backoff. They
pin the good behaviour so the fixes below can't silently regress it. After
the fixes: **36 passed**, stable across repeated runs; `npm run typecheck`
now covers the tests too.

## Headline results

| Behaviour (deterministic test evidence) | Before | After |
|---|---|---|
| Offer sent while signaling still CONNECTING (the shipped autoConnect profile) | **dropped silently, session never forms** | delivered once the socket opens |
| `await signaling.connect()` contract | resolves while socket still CONNECTING | resolves only when OPEN; concurrent callers share the attempt; rejects on failure |
| Remote ICE candidates arriving before the answer is applied (3 in test) | **3/3 lost** (browser throws, error swallowed) | 3/3 queued and applied in order |
| Mid-session `RTCPeerConnection` `failed` | permanent, silent outage | automatic re-offer after `reconnectDelayMs` (default 2 s) |
| Signaling restored while the session never completed | permanent, silent outage | automatic re-offer |
| Unhandled promise rejections (autoConnect failure; malformed answer SDP) | 2 reproducible paths | 0 |
| Late `close` from a superseded socket | spurious `disconnected` + double reconnect | ignored |
| Payload with `bbox: ["a","b","c","d"]` or `width: "640"` | passed the guard → NaN into `DetectionMath` | rejected at the boundary |
| Mirrored streams | no `invertX` support | `invertX` in `NormalizeOptions`, composes with `invertY` |
| Qualifier ticking with no frame provider | silently inert forever | warns once, gate stays fail-open |

## The changes, one by one

### 1. `connect()` contract + the offer-drop race (L1)

**Problem.** `SignalingService.connect()` returned an already-resolved promise
when the socket was merely `CONNECTING`. The shipped profile uses
`autoConnect: true`, so when the host called `webrtc.connect()` (camera just
went Active) the sequence was: `await signaling.connect()` → resolves
instantly → offer created → `send()` sees the socket not OPEN → **drops the
offer with only a console warn**. With no retry anywhere, the session never
formed. Timing-dependent in the field; deterministic in the suite.

**Evidence (red).** Two tests: the direct contract
(`connect() does not resolve while CONNECTING` — `expected true to be false`)
and the end-to-end race using the **real** `SignalingService` + a mock pc
(`expected [] to have a length of 1` — no offer on the wire).

**Change.** `connect()` now tracks a `pendingOpen` promise per socket:
resolves on `onopen`, rejects on error/premature close, and is returned to
every concurrent caller while CONNECTING. Awaiting `connect()` now *means*
"safe to send".

### 2. Early ICE candidate queue (L2)

**Problem.** Candidates arriving while `setRemoteDescription` was still
in flight hit the browser's "remote description was null" error, which was
caught and logged — the candidate was gone. On real networks the answer and
the first candidates arrive back-to-back; losing them can prevent
connectivity entirely (exactly the class of bug the aiortc-prefix work was
meant to bury).

**Evidence (red).** With `setRemoteDescription` deferred by the mock, three
trickled candidates: `expected [] to deeply equal ['candidate:a 1', …]`.

**Change.** Candidates received before `remoteDescriptionSet` are pushed to
`pendingCandidates` and applied **in order** after the answer lands (with a
staleness guard in case the session was replaced mid-flight).

### 3. No unhandled rejections (L3)

**Problem.** Two fire-and-forget paths could reject with nothing attached:
`void this.connect()` in `SignalingService.start()` (initial connect failure)
and `void this.onAnswer()` (malformed answer SDP → `setRemoteDescription`
rejects). Unhandled rejections crash Node hosts and land in the browser
console with no context.

**Evidence (red).** Both reproduced:
`expected [ Error: Signaling socket error ] to have a length of +0`.

**Change.** The autoConnect path attaches a `.catch` (the retry is onclose's
job); `onAnswer` wraps `setRemoteDescription` in try/catch and logs — a bad
answer is a protocol error, deliberately *not* auto-retried (it would loop).

### 4. Stale-socket close guard (L4)

**Problem.** `onerror` checked `this.socket === socket` but `onclose` did not
— a late close event from a superseded socket emitted a spurious
`disconnected` and scheduled a second reconnect against a healthy connection.

**Evidence (red).** `expected 1 to be +0` (disconnect count after a stale
close).

**Change.** All socket handlers now guard on identity; the close handler
still settles that socket's own open-promise (so no caller ever hangs) but
touches service state only if it is the current socket.

### 5. Session recovery (the missing story)

**Problem.** Signaling reconnected with backoff, but nothing ever re-sent an
offer, and a `failed` peer connection was never renegotiated. Any mid-session
drop — server restart, network blip, NAT rebind — was a **permanent, silent**
end to detections. (The review flagged this at both the library and client
level; fixing it here fixes every host.)

**Evidence (red).** `pc failed → expected 1 pc to be 2` and
`signaling restored → expected 1 offer to be 2`.

**Change.** `WebRTCService` now recovers in two cases, both funnelled through
one `scheduleReconnect(reason)`:
- `connectionState === 'failed'` → re-offer after `reconnectDelayMs`
  (default 2000 ms). `'disconnected'` is deliberately excluded — ICE can
  self-heal it, and reacting to it causes reconnect storms.
- signaling `connected` fires while a previously-attempted session is not
  `connected` → the server side has lost us (new server session), re-offer.

Guard rails: `autoReconnect: false` opts out; an intentional `close()` sets a
flag that suppresses recovery (tested); a pending timer is never doubled.

### 6. Wire-payload validation + typed `pts`

**Problem.** `isDetectionsPayload` never checked that `bbox` elements or
`frame`/`width`/`height` were numbers — `bbox: ["a","b","c","d"]` passed and
became NaN world coordinates downstream.

**Evidence (red).** Three failing validation tests, including the end-to-end
one: `DetectionService` forwarded two malformed payloads to subscribers.

**Change.** The guard now requires every numeric field to be a **finite
number** (`Number.isFinite`, so `null`/`NaN`/strings all fail), and
`DetectionsPayload` gained the optional `pts` field matching the server's
2026-07 addition — typed, documented, and accepted by the guard as
number/null/absent.

### 7. `invertX` (mirrored streams)

Review weakness: only `invertY` existed. `NormalizeOptions.invertX` (default
false) now mirrors both the center and the rect
(`rx = 1 - rx - rw`), composing with `invertY`. Covers front-facing cameras
and servers running `QVS_FLIP_HORIZONTAL=true`.

### 8. Qualifier: loud when inert

Review weakness: a host that wires the tick source but forgets
`setFrameProvider()` gets a qualifier that silently never reports (the exact
state the package shipped in before the quest-client wired it). It now warns
**once** ("…the qualifier is inert and shouldStream stays true") and stays
fail-open; setting a provider re-arms the warning.

## What was deliberately NOT changed

- **The DI-factory casts in `profile.ts`** (~10 `as` casts). The fix belongs
  upstream in `@realitycollective/service-framework` (generic `useFactory`
  typing); casting differently here would just move the hole.
- **Reacting to pc `'disconnected'`** — see §5; only `'failed'` triggers
  recovery.
- **Normalized-coordinate clamping** — out-of-frame boxes still produce
  out-of-[0,1] values; hosts get the raw geometry (documented behaviour).
- **The `data.includes('"ready"')` fast-path** in channel routing — quirky
  but correct and now baseline-locked by a test.

## Lessons for the guide

- **Mock the platform seam, not the library.** Stubbing global
  `WebSocket`/`RTCPeerConnection` made timing races (CONNECTING windows,
  deferred `setRemoteDescription`) *scriptable* — the difference between a
  deterministic red test and a flaky one. The mock should also **enforce the
  platform's contracts** (throwing `addIceCandidate` pre-answer) or the test
  can't catch the violation.
- **Lock baselines before fixing.** 21 of 36 tests were written to pass
  against the unmodified code, pinning reviewed-correct behaviour (math,
  prefix handling, backoff) so the fixes couldn't regress it unnoticed.
- **Assert the absence of unhandled rejections explicitly** with your own
  process listener; runner-level detection is nondeterministic across
  workers and can't be scoped per-test.
- **Fix recovery in the library, not the host.** The client app could have
  papered over the missing re-offer, but every future host would inherit the
  gap. Recovery semantics (when to re-offer, when not to) belong next to the
  state machine that knows them.

## Test inventory (36)

| Area | Tests |
|------|-------|
| Signaling (7) | connect() CONNECTING contract, already-OPEN reuse, error rejection, autoConnect unhandled-rejection, stale-close guard, backoff reconnect, user-disconnect no-reconnect, send drop-safety |
| WebRTC (8) | offer-across-race (real signaling), early-candidate queue, malformed-answer rejection, pc-failed recovery, signaling-restored recovery, intentional-close no-recovery, ICE prefix both ways, ready/detection routing |
| Detection (6) | guard accepts valid/pts/null-pts/empty, rejects bad bbox elements, rejects non-numeric frame/width/height, rejects wrong shapes, service emits + tracks frame size, service drops malformed |
| Math (10) | center/rect invertY on/off, resolution independence, zero-frame guard, invertX ×3, dedup policies ×3 |
| Qualifier (4) | warn-once when inert, too-dark gate closes, normal gate opens, fail-open default |
