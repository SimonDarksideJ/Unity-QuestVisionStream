# V2 Architecture & Technical Review — July 2026

Scope: the `V2/` tree only (Python inference server, `com.questvisionstream`
TypeScript library, `quest-client` IWSDK app, docs, and the deploy pipeline).
Review only — no code was changed. Every finding below was verified against the
actual source with file/line references.

**Overall verdict:** the V2 rebuild is architecturally sound and considerably
better engineered than V1 — clean layering, a real DI seam, protocol fidelity,
pinned dependencies, and a working CI/deploy story. The weak points cluster in
four places: (1) the server runs inference **on the asyncio event loop**,
(2) the client has **no recovery path** for any mid-session failure (plus one
real signaling race), (3) there is **zero automated testing** despite the
codebase being full of trivially testable pure functions, and (4) the security
posture assumes a trusted LAN while the docs actively promote internet exposure.

---

## 1. What works well

### Server (`V2/QuestVisionStreamServer/`)

- **Detector registry as single source of truth** — `detectors/__init__.py`
  maps names → lazy factories, and `server.py` derives the CLI `choices` from
  the registry, so the V1 `owl2`≠`owlv2` dispatch bug class is structurally
  impossible now.
- **Lazy, per-detector imports + split pinned requirements** — a YOLO-only
  deployment never imports torch/transformers/mediapipe; all `requirements*.txt`
  are `==`-pinned. Clean, reproducible dependency story.
- **Clean layering** — `base.py` owns the wire `Detection` type,
  device selection (MPS→CUDA→CPU), and box clamping; detectors return
  detections only; drawing is isolated to the optional display path;
  `WebRTCServer` has no detector knowledge (processor factory injection).
- **Good deployment hygiene** — minimal headless Dockerfile with a real
  `HEALTHCHECK`, `run-local.sh` sets the correct MPS env, dependency-free
  health endpoint. Florence-2's dtype/device handling (`_move`) is a
  thoughtful detail that avoids a common transformers-on-accelerator crash.

### Library (`V2/com.questvisionstream/`)

- **Token-based DI with symmetric lifecycle** — every service subscribes in
  `start()` and unsubscribes in `destroy()`; interfaces don't leak
  implementation types (`IWebRTCService` never exposes `RTCPeerConnection`);
  clean value/type export split in `index.ts`. Host-swappable by token, which
  is the stated design goal — and the quest-client host proves it works.
- **The aiortc `candidate:` prefix quirk is handled correctly** —
  strip-on-send / re-add-on-receive, both idempotent
  (`WebRTCService.ts:25-31,94,135`). This was the #1 interop landmine
  identified in the V1 evaluation and it is done right.
- **Detection math is correct** — center normalization, the vertical-flip rect
  math, div-by-zero guards, and per-frame server `width/height` normalization
  (adaptive-resolution safe) all check out (`DetectionMath.ts:29-45`).
- **Strong TS strictness** — `strict`, `noUncheckedIndexedAccess`,
  `noImplicitOverride` all on and honored.
- **`@realitycollective/service-framework@1.0.0` is real and published** on
  npm (verified against the registry + lockfile integrity), correctly declared
  as peer + dev dependency.

### Quest client (`V2/quest-client/`)

- **Thin-host pattern executed as designed** — systems resolve services by
  token only; `DetectionRenderSystem` is purely event-driven (no per-frame
  `update()`); camera acquisition correctly polls `CameraSource` state and
  only hands the stream to WebRTC once `Active`.
- **The edge quality gate is genuinely wired end-to-end** — the host feeds
  pixels via `setFrameProvider` (`CameraStreamSystem.ts:74`) and toggles
  `videoTrack.enabled` from `shouldStream` (`:58-62`); the qualifier defaults
  to "stream" before the first sample so startup isn't blocked. This is a
  clean recreation of the Unity `BrightnessEstimation` intent.
- **Config resolution is layered and race-free** — `?server=` → `/api/config`
  (KV live / env var) → `VITE_SIGNALING_URL` → localhost; the fetch is awaited
  before `World.create`; both the Pages Function and the client opt out of
  caching so KV edits are live-on-reload as documented.
- **CI/deploy isolation holds** — production deploys only from the `IWSDK`
  branch, staging PRs go to a separate Pages project, project bootstrap is
  idempotent, fork PRs (no token) skip deploys cleanly.
- **Local library linking is done right** — `file:` dep + Vite/tsconfig alias
  to source gives one type graph and HMR while keeping the framework dep in
  `node_modules`; `npm ci` is deterministic.

### Docs

The `V2/Documentation/` hub is unusually good: the architecture diagram matches
the code, the connectivity guide covers the real failure modes (mixed content,
TURN, KV vs env var), and the hosting trade-offs (native MPS vs Docker, no
Cloudflare GPU) are correctly reasoned.

---

## 2. What doesn't work (bugs)

### Server

> **Status update (2026-07):** S1–S5 and the server-side robustness/security
> items below were fixed test-first in the server hardening pass — see
> [improvements/2026-07-Server-Hardening.md](improvements/2026-07-Server-Hardening.md)
> for the before/after evidence. The table is kept as originally written for
> the historical record.

| # | Severity | Location | Issue |
|---|----------|----------|-------|
| S1 | **High** | `video_processor.py:91` | **Synchronous inference blocks the entire event loop.** `self.detect(img)` runs the full model forward pass inline in the coroutine — no `run_in_executor`, no worker thread. During inference (seconds for Florence-2 on CPU) *everything* freezes: other peers, websocket ping/pong (risking self-inflicted ping timeouts), ICE, and the health endpoint. This is the single most consequential defect in V2. |
| S2 | **High** | `server.py:33-40` vs `webrtc_server.py:50-51` | **Detector state is shared across all connections**, while the factory comment claims "each connection gets a fresh VideoProcessor + detector state." `Florence2Detector._frame_count`/`_last` and `BodyTracker.pose` (stateful MediaPipe tracking) are shared — two concurrent clients scramble frame-skip cadence and cross-serve cached detections. Either give each connection its own detector or enforce/document single-client. |
| S3 | Med | `webrtc_server.py:103-119` | **The `icecandidate` trickle handler is dead code** — aiortc (≤1.9.0) never emits that event; it gathers ICE fully before the answer. It works today only because candidates ride in the answer SDP, but the handler misleads maintainers into thinking trickle is implemented server→client. |
| S4 | Low | `video_processor.py:76` + `config.py:62` | `QVS_LOG_INTERVAL=0` → `ZeroDivisionError` on the first frame, silently killing the stream (swallowed by the broad `except`). No lower bound on the env int. |
| S5 | Low | `webrtc_server.py:84-87` | Only the last video track's task is retained/cancelled; a second negotiated track leaks a running `recv` loop. |

### Library

> **Status update (2026-07):** L1–L4, the payload-validation and invertX
> weaknesses, the qualifier silent-inert trap, and the missing session-recovery
> story were fixed test-first in the library hardening pass — see
> [improvements/2026-07-Library-Hardening.md](improvements/2026-07-Library-Hardening.md).
> Kept as written for the historical record.

| # | Severity | Location | Issue |
|---|----------|----------|-------|
| L1 | **High** | `SignalingService.ts:58-63` + `WebRTCService.ts:77,107` | **Offer silently dropped in the autoConnect race.** `connect()` resolves immediately when the socket is merely `CONNECTING`, but `send()` drops anything unless `OPEN`. If the camera goes Active before the WS handshake completes (or during a reconnect window), the offer is logged-and-dropped and — because there is no retry/renegotiation anywhere — the session never connects, silently. Timing-dependent but real, and its failure mode is permanent. |
| L2 | **High** | `WebRTCService.ts:125-142` | **Early remote ICE candidates are lost.** Candidates arriving while the answer's `setRemoteDescription` is still pending throw inside `addIceCandidate` and are swallowed by the catch. No pending-candidate queue (the standard fix). Can prevent connectivity on real networks. |
| L3 | Med | `SignalingService.ts:53`, `WebRTCService.ts:62` | Unhandled promise rejections: `void this.connect()` on start has no `.catch()` (initial connect failure → unhandled rejection); `void this.onAnswer(...)` rejects uncaught on malformed SDP. |
| L4 | Low | `SignalingService.ts:82-86` | `onclose` doesn't guard against a superseded socket (unlike `onerror`), so a late close from an old socket can emit a spurious `disconnected` and double-schedule reconnects. |

### Quest client

> **Status update (2026-07):** C1, C3 (capture-pose approximation), C4, C5,
> the config-fetch timeout, `?server=` validation, and the missing status
> surface were fixed test-first in the client hardening pass — see
> [improvements/2026-07-Client-Hardening.md](improvements/2026-07-Client-Hardening.md).
> C2's recovery logic landed in the library pass; the host now surfaces
> failures instead of swallowing them. Kept as written for the historical
> record.

| # | Severity | Location | Issue |
|---|----------|----------|-------|
| C1 | **High** | `CameraStreamSystem.ts:49-52` | **`CameraState.Error` is never handled.** Permission denied / capture failure → the system polls forever, no retry, no user feedback. Silent permanent stall on the most common first-run failure. |
| C2 | **High** | whole client | **No WebRTC recovery path.** Signaling reconnects with backoff, but nothing re-sends the offer, and nothing subscribes to `stateChange` to renegotiate a `failed`/`disconnected` peer connection. Any mid-session media drop permanently kills detections. `beginStreaming` fires `void webrtc.connect()` once; the rejection is swallowed. |
| C3 | **High** (vs V1 parity) | `DetectionRenderSystem.ts:64-75` | **No pose-freeze.** Placement rays are cast from the head pose at *detection-arrival* time, not capture time — after the full capture→encode→inference→return round trip. Under head motion tags land wherever you're looking when the reply arrives. The Unity reference marks capture-pose snapshotting as P0. |
| C4 | Med | `DetectionRenderSystem.ts:78-86` + `TagFactory.ts:62-64` | **Label `CanvasTexture` leak** — `SpriteMaterial.dispose()` doesn't dispose `.map`; every disposed tag leaks a GPU texture. (`setTagLabel` disposes the old map correctly, so the pattern exists elsewhere.) Low impact today, bites as soon as tag churn is added. |
| C5 | Med | `index.html:35` vs `config.ts:26` | **The in-app hint tells users `?server=ws://HOST:3000`** — blocked as mixed content on the deployed HTTPS page. The docs get it right; the on-device hint produces the exact silent failure the docs warn about. |

---

## 3. Security posture

The system's threat model is "trusted LAN," but the docs promote tunnels, HF
Spaces, and public demo topologies. If internet exposure is a real use case,
these need addressing:

1. **No auth on the signaling WebSocket** (`webrtc_server.py:55`, binds
   `0.0.0.0:3000`). Anyone with the URL can negotiate a session and pin the
   GPU. No token, no Origin check (CSWSH-able from any web page a LAN user
   visits), no connection cap.
2. **`?server=` is an unauthenticated camera redirect** (`config.ts:53-57`).
   A crafted link/QR silently points the headset's passthrough stream at an
   attacker's server — and the intended UX is "scan a QR on the Quest."
   Minimum: an on-screen "streaming to HOST — continue?" confirmation.
3. **Container runs as root** (no `USER` in the Dockerfile).
4. Minor: health endpoint answers any path on all interfaces with detector +
   connection-count info; unbounded header read is a mild slowloris surface.

---

## 4. What could be improved

### Robustness / correctness hardening

- **Server: latest-frame-wins backpressure.** `process_video_stream` awaits and
  processes every frame sequentially; under load latency grows without bound.
  Drain the track and infer on the freshest frame only. Related: report
  `frame.pts` (media timestamp) instead of a server-side counter so the client
  can correlate detections to captured frames (this is also what pose-freeze
  (C3) needs on the wire).
- **Server: per-message signaling validation.** One malformed JSON message
  currently tears down the whole peer connection via the broad outer `except`
  (`webrtc_server.py:122-141`).
- **Server: silent `except Exception: pass`** in the data-channel send, ICE,
  and health paths hide real field failures — log them.
- **Library: validate payload numerics.** `isDetectionsPayload` doesn't check
  bbox element types or `width`/`height`/`frame` being numbers — a bad payload
  yields `NaN` coordinates downstream.
- **Client: config fetch has no timeout** — a hung `/api/config` blocks the
  entire boot at a black screen. Race it against a timer with fallback.
- **Client: no user-facing status at all.** The library emits everything
  needed (`stateChange`, `connected`/`disconnected`, `quality`) but the host
  renders none of it; every failure signal goes to an invisible console. On a
  headset this is the biggest UX gap.
- **Client: tag lifecycle is place-once-forever.** `per-class` dedup pins the
  first instance permanently; `clear()`/`setTagLabel` are never wired; nothing
  handles the WebXR reference-space `reset` event, so tags are misplaced after
  a recenter. YOLO's `half=true` should also be gated to CUDA on the server
  side (errors on CPU/MPS).

### Architecture / design

- **Testing is the systemic gap.** Zero tests in all three packages, and CI
  never touches the Python server. The irony: the highest-risk surfaces are
  pure functions begging for unit tests — `clamp_box`, the detector registry,
  config env parsing, `DetectionMath`, the ICE prefix helpers,
  `isDetectionsPayload`. A shared wire-protocol fixture (one JSON payload
  asserted identically by pytest and vitest) would lock in the "one server,
  both clients" contract that the whole design leans on.
- **DI factory casts.** `profile.ts` needs ~10 `as` casts
  (`ServiceActivationContext<…>`, dependency re-casts) that defeat the type
  system exactly at the wiring seam. Worth pushing generic factory typing
  upstream into the service framework, since that's the one place typing
  matters most.
- **Config split-brain on the server.** `config.py` presents `QVS_*` as
  centralized, but every detector reads its own `os.getenv` knobs ad hoc —
  a dozen documented env vars bypass `ServerConfig`, untyped and invisible to
  the health endpoint.
- **Tags side-step the ECS.** Detection tags are raw `THREE.Object3D`s added
  to the scene, invisible to IWSDK subsystems (grab, anchors, persistence).
  Fine today; re-model as entities before adding anchoring/interaction.
- **Enabled-but-unused XR features.** `hitTest` and `environmentRaycast` are
  requested at `World.create` but never used (placement is fixed 2 m). Either
  implement surface anchoring (the acknowledged next step) or drop the flags.
- **Deploy fragility worth a comment.** The Pages Function reaches Cloudflare
  only because wrangler's cwd happens to contain `functions/` from the branch
  checkout — it's not in the tested build artifact. One workflow comment plus
  a `curl /api/config` smoke check would de-fragilize it.
- **Qualifier wiring is silently optional.** The gate works because the host
  remembers to call `setFrameProvider`; a host that forgets gets a silently
  inert service (`shouldStream` stuck `true`). Consider logging a warning if
  ticks arrive with no provider, or folding the provider into the config.

### V1 feature parity still open (acknowledged in the README)

Pose-freeze (P0, = C3), surface anchoring via hit-test/depth (P0),
recenter cleanup, controller commit-on-input interaction, the 2D outline-box
overlay mode, and spatial-per-class dedup (library supports it; app hard-codes
`per-class`).

---

## 5. Priority order (recommended)

1. **S1 — move inference off the event loop** (`run_in_executor` / worker
   thread). Everything about multi-client behavior, latency, and keepalive
   stability is downstream of this.
2. **L1 + L2 — fix the connect race and buffer early ICE candidates.** These
   are the two ways a session fails to establish silently.
3. **C1 + C2 + user-facing status — the failure-recovery story.** Handle
   `CameraState.Error`, renegotiate on `failed`/`disconnected`, and surface
   state in-headset. Today every failure mode is permanent and invisible.
4. **Backpressure + `frame.pts` on the server, pose-freeze on the client
   (C3).** Together these fix "tags drift behind reality" — the core
   product-quality issue.
5. **Minimal test suite + CI for all three packages**, anchored by a shared
   wire-protocol fixture.
6. **Security pass gated on exposure plans:** WS auth token + Origin check +
   connection cap, `?server=` confirmation prompt, non-root container.
7. Cleanups: dead trickle handler (S3), texture disposal (C4), `ws://` hint
   (C5), `QVS_LOG_INTERVAL=0` guard (S4), config split-brain, unused XR
   feature flags.
