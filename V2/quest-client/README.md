# QuestVisionStream — WebXR Quest Client

> 📖 Full docs hub: [`../Documentation/`](../Documentation/README.md).

A **Meta IWSDK** (Immersive Web SDK, `@iwsdk/core`) WebXR app that recreates the
Unity Quest client's behaviour in the browser. It captures passthrough camera
frames, streams them to `QuestVisionStreamServer` over WebRTC, and renders
returned detections as world-anchored tags.

This is a **fresh recreation**, not a port of the Unity project — the Unity
client is reference only (see `../../UNITY_REFERENCE_FEATURES.md`). All streaming
logic lives in the reusable, **file-linked** [`com.questvisionstream`](../com.questvisionstream)
library; this app is the thin IWSDK host that wires it in.

## Architecture

```
IWSDK World (Three.js + ECS)
├── ServiceBridgeSystem    → (from @realitycollective/service-framework-iwsdk)
│                            per-frame ticks + XR focus/pause into the ServiceManager
├── CameraStreamSystem     → CameraSource → MediaStream → IWebRTCService
│                            + qualifier frame provider + edge quality gate
└── DetectionRenderSystem  → IDetectionRenderer: detections → world-anchored tags

RealityCollective Service Framework (DI) — services in the linked library
   ISignalingService → IImageQualifierService → IWebRTCService → IDetectionService
```

The service graph is stood up by `startServiceRuntime(world, …)` from
`@realitycollective/service-framework-iwsdk` (in `src/index.ts`), using the
library's `createQuestVisionStreamProfile`. Systems resolve services **by interface
token** from the `ServiceManager` (`manager.resolve(IWebRTCService)`) — they never
import a concrete service class.

## Key behaviours recreated from the Unity reference

- **World placement with capture-time pose** (`DetectionRenderSystem` +
  `PoseHistory`): the normalized viewport center of each box is unprojected
  through the camera pose from **~capture time** (arrival −
  `AppConfig.assumedLatencyMs`), not the pose when the reply arrives — the
  analogue of Unity's `DetectionSpawnerManager` + `CapturePosition()`
  pose-freeze. Per-class dedup via the library's `DetectionDeduper`.
- **Billboarded label tags** (`TagFactory`): a marker + canvas-text sprite, the
  analogue of the Unity `DetectionTag` prefab (full GPU disposal incl. label
  textures via `disposeTagObject`).
- **Edge quality gate** (`CameraStreamSystem`): toggles the outbound video
  track's `enabled` from the brightness qualifier — frames too dark/over-exposed
  aren't streamed. Recreates the intent of the Unity `BrightnessEstimation` sample.
- **Status surface** (`src/ui/status.ts`): camera state, signaling, WebRTC
  connection, quality gate, and detection throughput are shown on the page's
  status panel (fed by the library's events) — failures are never console-only.
  Camera permission errors are detected (`CameraState.Error`) and reported.

## Prerequisites

- Node 18+ and a modern browser. For on-device testing, a Meta Quest with the
  Horizon Browser reachable over LAN (WebXR needs a secure context — use HTTPS or
  IWSDK's managed dev runtime / an HTTPS tunnel).
- A running `QuestVisionStreamServer` (see `../QuestVisionStreamServer`).

## Run

```bash
npm install          # installs @iwsdk/core, three, and file-links the libraries
npm run dev          # Vite dev server on :5173 (host: all interfaces)
# open with the server target:  http://<dev-host>:5173/?server=ws://<server>:3000
npm run build        # tsc --noEmit + production bundle to dist/
npm run typecheck
npm test             # vitest — @iwsdk/core mocked at the module seam
```

Configure the signaling target via `?server=` (must be a `ws://`/`wss://` URL —
anything else is rejected; hosted HTTPS pages need `wss://`), the `/api/config`
Pages Function (KV/env, with a 4 s fetch timeout), or `VITE_SIGNALING_URL` at
build time (see `src/config.ts`). The resolved target is shown on the status
panel.

## Notes / limitations

- **Verified:** `tsc --noEmit` and a full `vite build` pass against the real
  `@iwsdk/core@0.4.2` + `three` — the World/System/Component/Camera API usage is
  type-correct. See [`../Documentation/IWSDK-API-Reference.md`](../Documentation/IWSDK-API-Reference.md)
  for the API this was built against.
- **Placement depth:** v1 places tags at a fixed distance along the detection ray.
  True surface anchoring (raycast the ray against IWSDK scene-understanding
  `XRMesh` / environment depth, mirroring Unity's `EnvironmentRaycast`) is the
  next enhancement — the seam and math are already in place.
- **Capture-pose latency = configured base + measured queuing delay**: the
  `LatencyEstimator` tracks the server's `pts` timestamps, so inference/network
  queuing spikes shift the pose lookup dynamically. Only the base
  (`assumedLatencyMs`, default 200 ms) remains a per-deployment constant.
- **Status is shown in-AR too**: `StatusSpriteSystem` renders the
  `StatusModel` headline as a head-locked sprite (hidden when healthy);
  legibility/placement tuning on a real headset is pending. The DOM panel
  covers the 2D page and desktop debugging.
- **Recenter-safe**: a WebXR reference-space `reset` clears all tags, the
  pose history, and dedup (everything anchored in the old space is invalid).
- **`?server=` overrides ask for consent** for non-localhost targets before
  the camera stream is pointed anywhere.
- The app enables `features.camera` and `features.environmentRaycast`; scene
  understanding can be added for mesh-accurate anchoring.
- Tests + hardening history: see
  [`../Documentation/improvements/2026-07-Client-Hardening.md`](../Documentation/improvements/2026-07-Client-Hardening.md).
  CI runs the suite on every PR/push touching `V2/` (`.github/workflows/v2-tests.yml`).

