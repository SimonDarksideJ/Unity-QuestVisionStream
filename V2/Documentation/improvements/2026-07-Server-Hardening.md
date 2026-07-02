# 2026-07 — Server hardening pass (`QuestVisionStreamServer`)

**Scope:** the Python inference server only (`V2/QuestVisionStreamServer/`).
**Driver:** the findings in
[`../Architecture-Review-2026-07.md`](../Architecture-Review-2026-07.md) §2
(server bugs S1–S5) plus the robustness/security items.
**Method:** red → green → measure, per the
[strategy guide](README.md). Every defect below was first *proven* by a failing
test against the unmodified code, then fixed, then re-measured.

## The red run (before any fix)

The new suite (`QuestVisionStreamServer/tests/`, 18 tests) against the original
code:

```
14 failed, 4 passed
```

The 4 passes were the baseline regression tests (detector registry contract,
`clamp_box`, env-int fallback) — expected to pass and locked in so they can
never silently break. The 14 failures are the defects, itemised below. After
the fixes: **18 passed** (stable across repeated runs, ~2.5s, no model
weights or GPU needed).

## Headline stats

| Measurement (test-printed `[STATS]`) | Before | After | Change |
|---|---|---|---|
| Max event-loop stall while running 100ms inferences | **316ms** | **8.4ms** | ~38× more responsive |
| Live-edge lag, 100fps feed into a 20fps detector (40 frames) | **1631ms**, growing linearly with stream length | **60ms max**, bounded ≈ one inference interval | unbounded → bounded |
| Stale frames inferred in the same scenario | 40/40 (every stale frame) | 9/40 (freshest only) | ~4.4× less wasted compute |
| Stream survival with `QVS_LOG_INTERVAL=0` | crashed on frame 1 (`ZeroDivisionError`), stream silently dead | survives, interval clamped ≥ 1 | fixed |
| Malformed signaling messages before session death | 1 (first bad message tore the session down) | ∞ (logged and skipped, session lives) | fixed |
| Track-processing tasks cancelled at teardown | last only (earlier ones leaked) | all | fixed |
| Unauthenticated / cross-origin / unbounded connections | all accepted | token gate (4401), Origin allowlist (4403), cap-with-supersede (4000) | closed |
| Slowloris against the health endpoint | connection held open forever | dropped after `HEADER_TIMEOUT_S` (5s), headers capped at 100 lines | closed |

## The changes, one by one

### 1. Inference off the event loop (S1 — the big one)

**Problem.** `VideoProcessor.process_video_stream` called `self.detect(img)`
inline in the coroutine. A model forward pass is synchronous CPU/GPU work;
while it ran, the *entire* asyncio loop was frozen — websocket keepalives,
ICE, the health endpoint, every other peer. A Florence-2 pass on CPU (seconds)
could outlast the 20s websocket ping window and make the server kill its own
connection.

**Evidence (red).** `test_inference_does_not_stall_event_loop` runs 8 frames
through a fake detector that sleeps 100ms (simulating a forward pass) while a
5ms-tick heartbeat task measures the longest gap in loop servicing:

```
[STATS] max event-loop stall during 8x100ms inference: 316.0ms
AssertionError: event loop stalled 316.0ms — inference is blocking the loop
```

Note the stall (316ms) exceeds a single inference (100ms): consecutive frames
compounded, starving the loop for multiple passes in a row.

**Change.** Preprocess + detect now run via
`loop.run_in_executor(...)` on a dedicated single-worker
`ThreadPoolExecutor` (`video_processor.py`). One worker was a deliberate
choice: it keeps the loop free *and* serializes access to the shared detector
model, which is not thread-safe.

**Result (green).** `[STATS] max event-loop stall during 8x100ms inference: 8.4ms`.

### 2. Latest-frame-wins backpressure

**Problem.** The frame loop processed every frame in arrival order. With
inference slower than the camera feed (the normal case — Quest streams
30–60fps, YOLO-on-M2 manages ~15–25fps), frames queue and the detection
stream drifts progressively behind reality — the AR overlay lags further and
further behind where objects actually are.

**Evidence (red).** `test_slow_inference_drops_stale_frames_not_freshness`
feeds 40 frames at 100fps into a 20fps detector via a buffered (jitter-buffer
style) fake track. The original code processed all 40 frames. Measured
honestly (see "masking" note below) with the executor fix held constant in
both variants:

```
FIFO (no backpressure):  processed=40/40, last-frame lag=1631ms (grows with stream length)
latest-frame-wins:       processed=9/40,  last-frame lag=53ms, max lag=60ms
```

**Change.** `process_video_stream` was split into a **reader task** that
continuously drains `track.recv()` keeping only the freshest frame, and a
**processing loop** that always infers the latest frame and drops anything
staler. Drop counts are tracked and reported in the periodic FPS log
(`Processed N (received M, dropped K)`).

**Result (green).** Lag bounded at ~one inference interval regardless of
stream length; ~4.4× fewer wasted forward passes in the oversubscribed case.

**Masking lesson.** The first "before" measurement of FIFO lag showed a
suspiciously healthy 68ms. Cause: defect #1 — the blocked loop starved the
test's frame *producer* too, so the queue never visibly grew. Two defects were
hiding each other. The honest comparison above applies the executor fix to
both variants so the only variable is the scheduling strategy.

### 3. `pts` on the wire (frame correlation)

**Problem.** The payload carried only a server-side counter (`frame`), which
says nothing about *which captured frame* a detection belongs to. The client
needs that linkage for capture-pose alignment (the V1 "pose-freeze" feature,
P0 in `UNITY_REFERENCE_FEATURES.md`) — without it, tags are placed against
the head pose at reply time, not capture time.

**Evidence (red).** `test_payload_carries_media_pts`:
`AssertionError: payload must carry pts for frame correlation`.

**Change.** The payload now includes `pts` — the media timestamp of the
processed frame in RTP clock units (90kHz), `null` if the source carries none.
`frame` keeps its meaning (received-frame ordinal), so its gaps now double as
a visible drop counter. **Additive only:** the Unity client (`JsonUtility`)
and TS client (`isDetectionsPayload`) both ignore unknown fields — verified
against their parsers before shipping.

### 4. Per-message signaling validation

**Problem.** The signaling loop did `json.loads(message)` then indexed
`data["type"]` / `data["sdp"]` bare. Any malformed message raised into the
session-level `except`, tearing down the whole peer connection — a
one-message denial of service (and a footgun for future client work).

**Evidence (red).** `test_malformed_signaling_messages_do_not_tear_down_session`
feeds six garbage messages: `handler consumed 1/6 messages — a malformed
message tore down the session`.

**Change.** Messages are decoded by `_parse_signaling_message` (must be a JSON
object with a string `type`), each handler validates its own fields
(`on_offer` requires string `sdp`; `on_candidate` requires the candidate
fields), and each message is processed inside its own `try/except` that logs
and continues. Unknown `type`s are ignored for forward compatibility.

### 5. Session gating: auth token, Origin allowlist, connection cap

**Problem.** The signaling socket accepted anyone: no token, no Origin check,
no limit. The docs actively promote tunnel/public topologies, where this means
anyone with the URL can stream video into the GPU indefinitely, and any web
page a LAN user visits can hijack the socket cross-site.

**Evidence (red).** Three tests: unauthenticated connect accepted with
`QVS_AUTH_TOKEN` set; `Origin: https://evil.example.com` accepted with an
allowlist set; second concurrent connection stacked onto the first
(`config lacks max_connections`).

**Change** (`config.py` + `webrtc_server.py`):

- `QVS_AUTH_TOKEN` — when set, the client must dial
  `ws(s)://host:3000/?token=<value>`; otherwise closed with **4401**. Chosen as
  a query parameter so existing clients need *zero code changes* — the token
  rides in the configured signaling URL.
- `QVS_ALLOWED_ORIGINS` — comma-separated allowlist checked against the WS
  `Origin` header; mismatch closed with **4403**. Empty = allow all (LAN).
- `QVS_MAX_CONNECTIONS` (default **1**) — at the cap, the **newest connection
  supersedes the oldest** (closed with **4000**), rather than rejecting the
  newcomer. Rationale: the detector model — and its *state*, for
  `florence2`/`body` — is shared across connections (architecture review S2),
  so concurrent streams corrupt each other; and a headset that slept
  mid-session leaves a half-open socket that would otherwise block its own
  reconnect for the 20–40s ping timeout. Supersede semantics make reconnects
  instant and enforce the single-stream assumption. All three default open/1
  to keep the trusted-LAN experience unchanged.

This also resolves review finding **S2** operationally: the misleading
"fresh detector state" comment was corrected, and shared-state corruption
can no longer occur under the default configuration.

### 6. All track tasks cancelled (S5)

**Problem.** `video_task` was a single variable overwritten per `track` event;
teardown cancelled only the last one. A second negotiated video track leaked a
running `recv()` loop forever.

**Evidence (red).** `test_all_track_tasks_are_cancelled_on_teardown` emits two
tracks on the peer connection: `['first'] track task(s) leaked at teardown`.

**Change.** `video_tasks: list[asyncio.Task]`; teardown cancels and awaits all.

### 7. Dead ICE-trickle handler removed (S3)

**Problem.** `@pc.on("icecandidate")` — aiortc never emits that event (it
gathers ICE fully before `createAnswer` resolves; candidates ship inside the
answer SDP). The handler was dead code that told maintainers server→client
trickle existed when it doesn't.

**Change.** Removed; the module docstring now states the actual behaviour so
the next client implementer doesn't wait for `candidate` messages that never
come. No test needed — deleting dead code; the signaling flow is covered by
the other tests.

### 8. Config clamps + CUDA-only FP16 + health hardening

- `QVS_LOG_INTERVAL=0` crashed the frame loop (`frame_count % 0`) and the
  broad exception handler silently ended the stream. Now clamped ≥ 1 at the
  config boundary (`_env_int(..., minimum=1)`), same for `QVS_MAX_CONNECTIONS`.
  Red: `stream died after 0 frames with QVS_LOG_INTERVAL=0` → green: all
  frames survive.
- `QVS_YOLO_HALF=true` was passed to Ultralytics on any device; FP16 errors on
  CPU and is flaky on MPS. New `resolve_half(requested, device)` in
  `detectors/base.py` grants it only on CUDA and logs when refusing. (Pure
  function — testable without Ultralytics installed.)
- The health endpoint read header lines forever (a silent client held the
  connection open indefinitely — slowloris) and swallowed all errors. Now:
  `HEADER_TIMEOUT_S = 5.0` around the request read, max 100 header lines, and
  errors are logged. Red: the slowloris test timed out waiting for the server
  to drop the connection; green: dropped at the timeout.
- `asyncio.get_event_loop()` → `get_running_loop()` (deprecated pattern).
- Dockerfile now creates and runs as a non-root user (`qvs`, UID 10001) with a
  writable `HOME` for the Ultralytics weight cache — this process is exactly
  the thing people put behind tunnels.

## What was deliberately NOT changed

- **Per-connection detector instances.** Loading a model per connection costs
  seconds and doubles memory; the connection cap + single inference worker
  achieves the same safety for the single-headset design. If multi-headset
  ever becomes a goal, that is a separate pass (per-connection instances or a
  batching queue).
- **CI wiring.** The suite is self-contained
  (`pip install -r requirements.txt -r requirements-dev.txt && python -m
  pytest tests/ -c tests/pytest.ini --rootdir=.`) but the workflow file lives
  above `V2/`, which was out of scope for this pass. Recommended next step: a
  job mirroring the quest-client deploy workflow's build gate.
- **Wire-format changes beyond additive `pts`.**

## Test inventory (18)

| Area | Tests |
|------|-------|
| Frame pipeline | loop-stall measurement, backpressure/lag measurement, `pts` correlation + wire-shape, `QVS_LOG_INTERVAL=0` survival |
| Signaling | malformed-message robustness, auth token, Origin allowlist, cap/supersede, multi-track teardown |
| Config | log-interval clamp, max-connections clamp/default, security defaults + origin list parsing, garbage-int fallback |
| Detectors | registry name contract, unknown-name error (the historical `owl2` typo), `clamp_box` bounds, CUDA-only FP16 |
| Health | normal probe + slowloris timeout |
