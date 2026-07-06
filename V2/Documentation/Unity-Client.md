# The V2 Unity Quest Client

The Quest client is a **Unity 6 (6000.3) Android app** for **Quest 3 / 3S**
(`V2/Unity-Quest-Client`) built on the reusable
**`V2/com.questvisionstream.unity`** UPM package. It replaces the retired IWSDK
WebXR client (Meta removed passthrough camera access from the browser) while
carrying over everything that client pioneered: pose-frozen coordinated box
drawing, pts-based latency estimation, the AprilTag enter/update/exit + rules
pipeline, connection recovery semantics, and the "failures are never
console-only" status surface.

## Architecture

Everything is a **RealityCollective Service Framework service** (single
responsibility), and every replaceable concern is a **service module** under a
parent service. Modules share a common child interface (e.g.
`IDetectionRenderModule`) but each concrete module registers under **its own
interface** (e.g. `IEphemeralBoxRenderModule`) — the framework registry does not
allow two registrations of the same interface.

```
ISignalingService (10)        WebSocket JSON: offer/candidate/status → · ← answer
ICameraStreamService (12)     ── ICameraCaptureModule: MetaPassthroughCameraCaptureModule (app)
IImageQualifierService (15)   ── IImageQualifierModule: BrightnessQualifierModule
IWebRTCService (20)           ── IWebRTCTransportModule: AndroidWebRTCTransportModule
IPoseTrackingService (25)     pose history + pts latency (pose-freeze)
IDetectionService (30)        validate/parse → normalized RenderBatch
IDetectionRendererService (35)── IDetectionRenderModule: EphemeralBoxRenderModule (package)
                                                          AnchoredTagRenderModule (app)
ITagDetectionService (40)     ── ITagDetectorModule: KeijiroAprilTagDetectorModule (41h12)
ITagRoutingService (41)       enter/update/exit + rules ("see X → do Y")
ITagPlacementService (42)     coloured tag markers
IStatusService (50)           status model → in-headset HUD + server uplink
```

`Assets/Scenes/QuestVisionStream.unity` contains the full working hierarchy:

- **XR Origin › Camera Offset › Main Camera** — Camera (solid-colour clear,
  alpha 0 for passthrough), `TrackedPoseDriver` (centre-eye bindings),
  **`ARCameraManager`** (this is what enables passthrough *and* sources camera
  frames under the OpenXR plugin — there are no OVR building blocks on this
  path), URP camera data, AudioListener.
- **AR Session** — `ARSession` (pairs with the enabled "Meta Quest: Session"
  OpenXR feature).
- **QuestVisionStream** — the **`GlobalServiceManager`** plus the
  `QuestVisionStreamBootstrap` component, which registers the service graph
  code-first (server URL, auth token, render mode, tag size etc. are its
  inspector fields). To go fully asset-driven instead, assign a
  `ServiceProvidersProfile` to the GlobalServiceManager and disable the
  bootstrap's registration — every service/module already takes a
  `(name, priority, profile, …)` constructor, so they configure from profile
  assets unchanged.

## Camera access (important)

**`WebCamTexture` does not work on Quest 3.** Camera frames come from Meta's
passthrough camera access, surfaced under the OpenXR plugin by the **"Meta
Quest: Camera (Passthrough)"** OpenXR feature (enabled in this project with
Camera Image Support) through AR Foundation:
`ARCameraManager.TryAcquireLatestCpuImage` → RGBA conversion → the shared
frame texture every other component samples. Requires **both**
`android.permission.CAMERA` and `horizonos.permission.HEADSET_CAMERA`
(declared in the manifest, requested at runtime), Quest 3/3S on HorizonOS v74+.

## Transport decision

`com.unity.webrtc` was **removed** from the project: Unity deprecated the
package, and it has known Quest failures (video track dead under Vulkan, a
startup clash with the Meta SDK's Vulkan hooks). The client keeps the V1
approach — a **minimal Android plugin** (`QuestVisionStreamPlugin.androidlib`
inside the UPM package) wrapping `io.github.webrtc-sdk:android` — but slimmed
to **media only**: the peer connection, the pixel-fed video track and the
`detections` data channel. Signaling is C# (`com.utilities.websockets`), so all
protocol logic is testable and in one place. The webrtc dependency resolves
from mavenCentral at Gradle time — no fat AAR in the repo.

Frame path (proven V1 pipeline): passthrough texture → blit to ≤640x480 →
GPU RGB→I420 (BT.601 compute shader) → async readback (latest-frame-wins) →
JNI → `PixelDataVideoCapturer` → hardware encoder.

### Stream orientation

The GPU readback convention delivers frames vertically flipped on-device (the
reason the V1 server defaulted `QVS_FLIP_VERTICAL=true`). The V2 client
corrects this **at source** — the flip is folded into the pump's existing blit
(`WebRTCServiceProfile.FlipStreamVertically`, default on), so the stream
arrives upright and the wire stays truthful for dumps/recordings/other
consumers. **Run the server with `QVS_FLIP_VERTICAL=false`.** The detection
`invertY` stays true either way — that converts image y-down to viewport y-up,
independent of stream orientation.

## Server discovery (Cloudflare KV, same as the IWSDK client)

The headset never needs a rebuild when the Mac's address changes. The flow is
identical to the web client's:

1. `tools/setup-tailscale-mac.sh` (run on the server host) publishes the live
   signaling URL — `wss://<machine>.<tailnet>.ts.net/?token=…` — into the
   Cloudflare KV namespace bound as `QVS_CONFIG` (key `signaling_url`).
2. The static Pages Function `GET https://questvisionstream.pages.dev/api/config`
   serves that value live (KV is read per request — changes apply in seconds).
3. `SignalingService` fetches that endpoint on **every (re)connect** (4 s
   timeout), adopts the published URL (embedded `?token=` included, `cid`
   appended), and falls back to the profile's static `ServerUrl` when the
   endpoint is unreachable or empty. Tokens are redacted from logs.

The remote-config endpoint is a `SignalingServiceProfile` /
bootstrap-inspector field; clear it to pin a static URL instead.

## Protocol (unchanged; V2 hardening honoured)

Same wire protocol as V1/the server — one server serves every client. The Unity
client implements the V2 rules: `?cid=`/`?token=` params, no reconnect on close
4000/4001, backoff 1/2/4/8 s, re-offer on `failed`/restored signaling, queued
early remote candidates, aiortc `candidate:` prefix strip (idempotent),
nullable `pts` (drives the latency estimator), per-payload width/height
normalization with `invertY` default on, `status` uplink + 15 s `__keepalive`.
See [Configuration-and-Connectivity](Configuration-and-Connectivity.md).

## Detection rendering — two switchable modes

- **Ephemeral Boxes** (default; WebXR-client parity): each payload redraws
  hollow outline boxes + billboarded `label 82%` text, corners unprojected
  through the **capture-time pose** (2 s pose ring buffer, lookup at
  `arrival − estimated latency`) at a fixed distance.
- **Anchored Tags** (V1 parity): persistent markers placed by raycasting the
  capture-pose ray against physics geometry (scene mesh/colliders when
  present, fixed-distance fallback), with `none`/`per-class`/`spatial-per-class`
  dedup.

Switch at runtime via `IDetectionRendererService.SetActiveModule(...)` or
`QuestVisionStreamBootstrap.SetRenderMode(...)`; the outgoing module fully
cleans up its scene objects and GPU resources. A tracking-space recenter clears
all visuals and pose history in both modes.

## AprilTags (on-device, nothing server-side)

Detector backend is Keijiro's `jp.keijiro.apriltag` (official AprilTag C
library, ARM64 native, full 6-DoF pose from tag size + camera FoV). It decodes
**tagStandard41h12** — regenerate printed tags with:

```bash
python3 V2/tools/generate-apriltags.py           # 41h12 (default)
```

The old 36h11 sheets from the WebXR era will NOT decode. The registry
(ids 0–9, Alpha…Juliet) is unchanged and mirrored by `TagRegistryAsset`.
Pipeline: sample → decode (120 ms throttle, request-time pose snapshot) →
routing (TTL 8 s enter/update/exit, known-tags-only filter, rules engine) →
placement (coloured translucent quads + labels).

## Building

1. Open `V2/Unity-Quest-Client` in Unity 6000.3+. The
   `com.questvisionstream.unity` package is file-linked; `jp.keijiro.apriltag`
   and the RealityCollective/Utilities packages resolve from OpenUPM. Unity
   will generate `.meta` files for the file-linked package on first import —
   commit them.
2. Set the server URL on the `QuestVisionStream` object in
   `Assets/Scenes/QuestVisionStream.unity` (Tailscale IP of the Mac, port 3000).
3. Build for Android (IL2CPP/ARM64 — already configured) and deploy to a
   Quest 3/3S. Grant both camera permissions on first run.
4. Run the server: `V2/QuestVisionStreamServer/run-local.sh`.

Package EditMode tests: Window → General → Test Runner (protocol, math, dedup,
latency, pose, status suites).

## Known follow-ups (need on-device verification)

- Passthrough CPU-image orientation vs the server's default `QVS_FLIP_VERTICAL`
  — if boxes are mirrored/flipped, flip `invertY` on the bootstrap or
  `QVS_FLIP_*` on the server.
- AprilTag pose scale uses the XR camera FoV unless
  `TagDetectionServiceProfile.VerticalFovOverrideDegrees` is set from the
  passthrough camera intrinsics (`ARCameraManager.TryGetIntrinsics`).
- Anchored mode benefits from adding an `ARMeshManager` (+ mesh colliders) for
  true depth anchoring; without it markers fall back to fixed distance.
- Immersive Debugger `DevAgentSettings.asset` was disabled and its committed
  access token cleared — rotate that token in your Meta dashboard.
