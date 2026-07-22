# com.questvisionstream.unity

The reusable Unity client library for **QuestVisionStream V2**: streams the Quest 3/3S
passthrough camera to the `QuestVisionStreamServer` over WebRTC and renders the
returned detections, plus a fully on-device AprilTag pipeline.

Everything is built as **[RealityCollective Service Framework](https://github.com/realitycollective/com.realitycollective.service-framework)
services** following single-responsibility, with **service modules** as the
extension seam — the WebRTC transport, camera capture, image qualifiers,
detection renderers and tag detector backends are all swappable modules.

📖 Full documentation: [`V2/Documentation/Unity-Client.md`](../Documentation/Unity-Client.md)
(architecture, service map, wire protocol, build & deploy).

## Service map

| Service (priority) | Responsibility | Modules |
|---|---|---|
| `ISignalingService` (10) | WebSocket signaling: offer/candidate/status out, answer in; backoff; close-code semantics; `cid`/`token`; keepalive | — |
| `ICameraStreamService` (12) | Camera acquisition + stream resolution | `ICameraCaptureModule` (app provides the Meta passthrough module) |
| `IImageQualifierService` (15) | Edge quality gate | `IImageQualifierModule` (`BrightnessQualifierModule`) |
| `IWebRTCService` (20) | Session orchestration, frame pump, renegotiation | `IWebRTCTransportModule` (`AndroidWebRTCTransportModule`) |
| `IPoseTrackingService` (25) | Pose history + pts latency estimation (pose-freeze) | — |
| `IDetectionService` (30) | Payload validation/parse → normalized batches | — |
| `IDetectionRendererService` (35) *(in `com.ethar.debugdrawingbbox`)* | One active render module; recenter cleanup | `IDetectionRenderModule` (`EphemeralBoxRenderModule`, `AnchoredTagRenderModule` — both in `com.ethar.debugdrawingbbox`) |
| `ITagDetectionService` (40) | Throttled on-device tag decode | `ITagDetectorModule` (`KeijiroAprilTagDetectorModule`, tagStandard41h12) |
| `ITagRoutingService` (41) | enter/update/exit lifecycle + rules engine | — |
| `ITagPlacementService` (42) | Coloured world-space tag markers | — |
| `ITagDetectionBridgeService` (43) | Republishes tag sightings into the detection pipeline as ClassName detections (label = registry class name) — tags drive rendering and training like server detections, offline-capable | — |
| `ITrainingStateService` (45) | The authoritative training flow (queue of expected classes over the detection pipeline) | — |
| `ITrainingModelPlacementService` (47) | Spawns a step's `modelRef` catalog prefab aligned to the step's AprilTag (`TagPoseFollower` keeps it aligned) | — |
| `IStatusService` (50) | Status model + HUD source + server uplink | — |

## Notes

- The Android WebRTC plugin (`Runtime/Plugins/Android/QuestVisionStreamPlugin.androidlib`)
  is **media-only** in V2 — signaling moved to C#. It resolves
  `io.github.webrtc-sdk:android` from mavenCentral at build time (no fat AAR in the repo).
- The AprilTag detector module compiles only when `jp.keijiro.apriltag` is
  installed (asmdef versionDefines) and decodes **tagStandard41h12** — print tags
  with `V2/tools/generate-apriltags.py`.
- Wire protocol is byte-identical to the V1 Unity client and the V2 server —
  additive changes only.
