# QuestVisionStream — WebXR Quest Client

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
├── ServicePumpSystem      → pumps the ServiceManager each frame
├── CameraStreamSystem     → CameraSource → MediaStream → IWebRTCService
│                            + qualifier frame provider + edge quality gate
└── DetectionRenderSystem  → IDetectionRenderer: detections → world-anchored tags

Service Framework (DI, in the linked library)
   ISignalingService → IImageQualifierService → IWebRTCService → IDetectionService
```

Systems resolve services **by interface token** from the shared `ServiceManager`
(`ServiceManager.instance.getService(IWebRTCService)`) — they never import a
concrete service class. Construction/registration happens once via the
`QuestVisionStreamClient` facade in `src/index.ts`.

## Key behaviours recreated from the Unity reference

- **World placement** (`DetectionRenderSystem`): the normalized viewport center
  of each box is unprojected through the XR camera and a tag is placed along the
  ray — the analogue of Unity's `DetectionSpawnerManager` +
  `ScreenPointToRayInWorld`. Per-class dedup via the library's `DetectionDeduper`.
- **Billboarded label tags** (`TagFactory`): a marker + canvas-text sprite, the
  analogue of the Unity `DetectionTag` prefab.
- **Edge quality gate** (`CameraStreamSystem`): toggles the outbound video
  track's `enabled` from the brightness qualifier — frames too dark/over-exposed
  aren't streamed. Recreates the intent of the Unity `BrightnessEstimation` sample.

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
```

Configure the signaling target via `?server=ws://HOST:3000`, or
`VITE_SIGNALING_URL` at build time (see `src/config.ts`).

## Notes / limitations

- **Verified:** `tsc --noEmit` and a full `vite build` pass against the real
  `@iwsdk/core@0.4.2` + `three` — the World/System/Component/Camera API usage is
  type-correct. See `IWSDK_API_REFERENCE.md` for the API this was built against.
- **Placement depth:** v1 places tags at a fixed distance along the detection ray.
  True surface anchoring (raycast the ray against IWSDK scene-understanding
  `XRMesh` / environment depth, mirroring Unity's `EnvironmentRaycast`) is the
  next enhancement — the seam and math are already in place.
- The app enables `features.camera` and `features.environmentRaycast`; scene
  understanding can be added for mesh-accurate anchoring.
