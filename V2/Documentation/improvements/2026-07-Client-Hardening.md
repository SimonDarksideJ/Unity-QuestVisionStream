# 2026-07 — Quest-client hardening pass (`quest-client`)

**Scope:** the IWSDK WebXR host app only (`V2/quest-client/`) — the third and
final subsystem of the V2 review loop, after the
[server](2026-07-Server-Hardening.md) and [library](2026-07-Library-Hardening.md)
passes.
**Driver:** the client findings in
[`../Architecture-Review-2026-07.md`](../Architecture-Review-2026-07.md)
(C1–C5) plus the config-robustness and `?server=` weaknesses. C2 (WebRTC
recovery) was already fixed at the library layer; this pass adds the host-side
halves (surfacing failures, not swallowing the connect rejection).
**Method:** red → green → measure, per the [strategy guide](README.md).

## Test infrastructure (new — the app had none)

Vitest + happy-dom, with two seams mocked:

- **`@iwsdk/core` at the module boundary** (`vi.mock`): `createSystem` returns
  a plain base class, `CameraState`/`CameraSource`/`CameraUtils` are simple
  fakes. The systems' *own logic* runs unmodified against fake entities and a
  real `THREE.Scene`/`PerspectiveCamera` — so placement math, state handling,
  and lifecycle are all exercised for real.
- **The service seam via `runtime.ts`**: a stub `ServiceManager` resolves
  interface tokens to fake services (and counts resolutions, which caught the
  per-frame `resolve()` in the hot path).
- happy-dom has no 2D canvas, so `HTMLCanvasElement.getContext` is stubbed
  just enough for `TagFactory`'s text rendering; `THREE.CanvasTexture` is real,
  so disposal semantics are the real ones.
- The library resolves **from source** via the same alias `vite.config.ts`
  uses — one type graph, no prebuilt dist needed (mirroring the app's own
  build).

## The red run (before any fix)

```
8 failed | 7 passed; 3 test files failed to load
(modules under test did not exist yet: src/ui/status, src/rendering/PoseHistory,
 and TagFactory.disposeTagObject)
```

Vite resolves even *dynamic* imports at transform time, so a missing module
fails the whole test file — for brand-new modules, "file cannot load" **is**
the red evidence. The 7 passes are baseline locks (config tier priority,
per-class dedup, tag construction, `setTagLabel`'s correct texture swap).
After the fixes: **30 passed**, stable across repeated runs (~1.4 s), and the
tests are included in `tsc --noEmit`.

## Headline results

| Behaviour (deterministic test evidence) | Before | After |
|---|---|---|
| Camera permission denied (`CameraState.Error`) | polled forever, silent, `connected=false` | poll stops; error reported on the status surface |
| Any failure signal (camera, signaling, WebRTC state, quality gate) | console-only — invisible on a headset | `StatusModel` + DOM status panel fed by the library's events |
| Tag placement pose | head pose at **reply arrival** (after the full round-trip) | pose from ~capture time via `PoseHistory` (arrival − `assumedLatencyMs`); test shows a 0.75 m placement error corrected to < 1 µm of the capture-pose ray |
| `?server=javascript:alert(1)` / `https://…` / garbage | dialed verbatim as the signaling target | rejected — only `ws:`/`wss:` accepted, same validation applied to `/api/config` values |
| `/api/config` hangs | boot blocked forever (black screen) | 4 s abort → falls through to the next config tier |
| Tag teardown | `SpriteMaterial.dispose()` leaked the label `CanvasTexture` | `disposeTagObject` disposes geometry + materials + texture maps |
| Rejected `webrtc.connect()` | swallowed by `void` | surfaced as `connection: failed …` on the status panel |
| Qualifier service resolution | token `resolve()` every frame (~72–120 Hz) | resolved once at stream start (verified by resolve-count) |
| In-app `?server=` hint | said `ws://HOST:3000` — blocked as mixed content on the deployed HTTPS page | says `wss://`, explains when plain `ws://` works; mixed-content combos also warn at resolve time |

## The changes, one by one

### 1. `CameraState.Error` handling (C1)

**Problem.** The camera poll only checked for `Active`. A denied permission
(or any capture failure) put the component in `Error` and the system spun
forever — no retry, no message, nothing. The single most common first-run
failure was a silent black screen.

**Evidence (red).** Two tests: the poll-counter test (`getValue` calls kept
growing after `Error`) and the status test (file couldn't load — no status
surface existed at all).

**Change.** An explicit `Error` branch: sets `cameraFailed` (stops the poll),
reports `camera: error — camera unavailable (check the browser permission)`
to the status surface, and logs once.

### 2. Status surface (`src/ui/status.ts`)

**Problem.** The review's biggest UX finding: the library emits every event a
UI needs (`stateChange`, `connected`/`disconnected`, `quality`, `detections`)
and the host subscribed to none of them for display — all failures went to a
console nobody can see on a headset.

**Change.** Three small pieces, each independently tested:
- `StatusModel` — named fields, change-only notification, stable-ordered
  lines. One app-wide instance (`status`) that systems report into.
- `bindStatusDom` — renders the model into the page's new `#status` panel
  (visible on the 2D page before entering AR and in desktop debugging).
- `wireStatusServices` — subscribes the model to all four library services'
  events; returns one unsubscribe.

**Known limit (deliberate):** the DOM overlay is not visible *inside* an
immersive session. A world-space rendering of the same model (a
camera-anchored sprite reusing `TagFactory`) is the follow-up — it needs
on-headset validation, and the model/wiring built here is exactly what it
will consume.

### 3. Capture-pose placement (C3 — the P0 parity gap)

**Problem.** `placeOnRay` read `world.camera` at detection-*arrival* time —
after capture → encode → WebRTC → inference → data channel. Under head motion
tags land wherever you happen to be looking when the reply arrives. The Unity
reference calls capture-pose snapshotting P0.

**Evidence (red).** With fake timers driving `performance.now`: camera at
pose A, 200 ms pass, camera moves to pose B, detections arrive → the tag was
placed 0.75 m from the correct (pose-A) ray.

**Change.** `PoseHistory` (new, pure, fully unit-tested): the render system
records cloned camera matrices every frame (bounded 2 s window); detections
are unprojected through the snapshot nearest to
`arrival − AppConfig.assumedLatencyMs` (default 200 ms) via
`unprojectThroughSnapshot` — verified to reproduce `THREE`'s own unproject
math to < 1 µm. Falls back to the live camera when no history exists.

**Honest limits:** the latency is an assumed constant, not measured — the
server's `pts` field (added in the server pass) is the hook for estimating it
per-session later. This is capture-pose *approximation*, not full pose-freeze
with per-frame correlation; it removes the bulk of the error (head motion
during the round-trip) with zero wire changes.

### 4. Config robustness (`?server=` validation + fetch timeout)

**Problem.** Two review items: `resolveSignalingUrl` returned any `?server=`
value verbatim (a crafted QR/link could silently point the passthrough
stream anywhere — and the whole UX is "scan a QR"), and the `/api/config`
fetch had no timeout, so a hung network stalled boot forever.

**Evidence (red).** `javascript:alert(1)`, `https://attacker.example/collect`,
and `not a url` were all returned as the signaling target; the timeout test's
boot promise never settled.

**Change.** `isValidSignalingUrl` accepts only `ws:`/`wss:` URLs; invalid
values from *any* tier (query, `/api/config`, build-time env) are warned about
and skipped, falling through to the next tier. The fetch gets an
`AbortController` with `CONFIG_FETCH_TIMEOUT_MS = 4000`. Mixed-content combos
(HTTPS page + non-localhost `ws://`) warn at resolve time, and the resolved
server is shown on the status panel — so where the camera streams to is
always visible. A full "streaming to HOST — continue?" confirmation remains
open; scheme validation + visibility is the mitigation this pass ships.

### 5. GPU texture disposal (C4)

**Problem.** `disposeObject` disposed geometries and materials, but
`Material.dispose()` does not dispose `.map` — every torn-down tag leaked its
label `CanvasTexture` (the fix already existed in `setTagLabel`, three lines
away).

**Change.** `disposeTagObject` (moved into `TagFactory`, exported, tested)
disposes geometry, materials, *and* material maps; `DetectionRenderSystem.clear()`
uses it. Verified via THREE's real `dispose` events.

### 6. Host-side connect failure surfacing + hot-path resolve

`beginStreaming` no longer fires `void webrtc.connect()` — the rejection is
caught and reported (`connection: failed: …`); recovery itself is the
library's job (previous pass). The qualifier service handle is cached at
stream start instead of being token-resolved every frame (the resolve-count
test pins ≤ 2 resolutions per session).

### 7. The `ws://` hint (C5)

`index.html` now tells users `?server=wss://HOST:3000` and explains that
plain `ws://` only works from `http://localhost` — the old hint sent deployed
users straight into the silent mixed-content failure the docs warn about.

## What was deliberately NOT changed

> **Status update:** the first three deferrals below were completed in the
> follow-up pass after the IWSDK APIs were source-verified from the installed
> package — see [2026-07-Review-Completion.md](2026-07-Review-Completion.md).

- ~~**Recenter cleanup**~~ *Done in the completion pass* — `world.renderer.xr`
  → reference-space `reset` was verified in the installed 0.4.2 typings and
  wired to `clear()` + pose-history reset, with tests.
- ~~**In-AR world-space status HUD**~~ *Done in the completion pass* —
  `StatusSpriteSystem`, head-locked via the verified `playerHeadEntity`.
- ~~**`?server=` user confirmation dialog**~~ *Done in the completion pass.*
- **Surface anchoring via hit-test/depth** (the other P0) — orthogonal to
  this pass; the renderer seam and now the pose history are the foundations
  it will build on. Requires on-headset iteration.
- **Tag update/expiry policy** (`per-class` still pins the first instance
  forever) — a product decision, not a defect; `spatial-per-class` is one
  config value away.

## Lessons for the guide

- **Mock the framework, test the system.** Mocking `@iwsdk/core`'s
  `createSystem` with a bare base class let the real system logic run against
  real three.js objects — the placement test caught a 0.75 m error in actual
  matrix math, not in a simulation of it.
- **Typecheck sees the real types even when runtime sees the mock.** The
  mocked base class is parameterless but the real one isn't; instantiate via
  a cast at one documented seam rather than loosening the mock.
- **Assert against the instance the code under test holds.** A fake
  `getVideoTracks()` that built a fresh track per call made the gate test
  toggle a different object than it inspected — fixture identity bugs look
  exactly like product bugs until you check which object moved.
- **Vite resolves dynamic imports at transform time** — a not-yet-created
  module fails the whole test file, so for new-module red tests, plan for
  file-level red rather than per-test red.

## Test inventory (30)

| Area | Tests |
|------|-------|
| Config (10) | tier priority ×4 (baselines), `?server=`/config validation ×4, fetch timeout, — |
| Status (4) | model set/notify/lines, DOM binding, service wiring, unsubscribe |
| PoseHistory (5) | nearest lookup, empty, eviction, snapshot immutability, unproject ≡ THREE |
| DetectionRender (3) | capture-pose placement, per-class dedup, clear + texture disposal + dedup reset |
| CameraStream (6) | error stops poll, error on status, stream/qualifier wiring, quality-gate toggle, no per-frame resolve, connect-failure surfacing |
| TagFactory (3) | construction, `setTagLabel` swap+dispose, `disposeTagObject` full disposal |
