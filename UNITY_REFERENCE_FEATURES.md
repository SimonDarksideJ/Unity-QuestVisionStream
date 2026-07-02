# Unity Reference — Client-Led Feature Catalog for the IWSDK Recreation

Written 2026-07-01. The Unity project is **reference only**. The IWSDK/TypeScript
app is a **fresh recreation**, not a port. This catalogs the client-led
behaviour worth recreating, found by examining **every** sample scene in the
repo, and maps each to a WebXR/IWSDK equivalent.

## Scenes examined

| Scene | Location | Relevance |
|-------|----------|-----------|
| **StaticObjectDetection** | `com.questvisionstream/Samples~` + `Assets/Samples` | **The QVS product client** — server detections → world-anchored 3D tags. Primary reference. |
| **MultiObjectDetection** | `Assets/_ExternalAssets/PassthroughCameraApiSamples` | Meta's on-device (Sentis) detector. Best reference for **2D box+label drawing**, canvas-at-camera-pose, pooling, commit-on-input. |
| **CameraToWorld** | ″ | Camera↔world ray math, frustum/ray debug viz, snapshot(freeze) mode. |
| **CameraViewer** | ″ | Minimal feed→RawImage + permission gating. |
| **BrightnessEstimation** | ″ | **Image qualifier** (luminance) — relevant to the server's "qualify images" goal. |
| **ShaderSample / StartScene** | ″ | Passthrough shader FX; menu/laser-pointer navigation. Low priority. |

---

## Two rendering models in the reference (pick per UX goal)

1. **World-anchored 3D tags** — *QVS `DetectionSpawnerManager`*. Each detection is
   raycast onto the environment mesh and a persistent 3D tag is spawned at that
   world point (spins, label billboards to camera). Good for "label real objects
   in the room."
2. **2D outline boxes on a camera-aligned canvas** — *Meta `SentisInferenceUiManager`*.
   Hollow rectangles + labels drawn on a canvas frozen at the frame's camera pose.
   Good for "HUD-style live overlay that tracks the video."

The recreation should implement (1) as the default (it's the QVS behaviour paired
with the streaming server) and can borrow (2)'s drawing/pooling/pose-freeze
techniques.

---

## Core algorithm — bbox → world placement (MUST recreate)

The single most important client-led piece. From
`DetectionSpawnerManager.ComputeWorldPosition` (and mirrored in
`SentisInferenceUiManager.DrawUIBoxes`):

```
cx,cy      = bbox center in STREAM pixels
nx,ny      = cx/streamW , cy/streamH        # normalize by the FRAME's dims
if invertY: ny = 1 - ny                     # server frame is v-flipped
px,py      = nx*camRes.x , ny*camRes.y      # scale to camera intrinsics res
ray        = ScreenPointToRayInWorld(eye, (px,py))
worldPos   = EnvironmentRaycast(ray)        # depth/scene mesh hit
```

**Recreation notes (WebXR):**
- `streamW/streamH` come **per-frame from the server payload** (`width`/`height`),
  not a constant — the server ramps resolution up over time (see "adaptive
  resolution" below). Normalize by the frame that produced the detection.
- Replace `ScreenPointToRayInWorld` (Meta intrinsics) with a ray built from the
  `getUserMedia` camera's projection, aligned to the XR view.
- Replace `EnvironmentRaycast` (Meta Depth API) with **WebXR hit-test**
  (`XRSession.requestHitTestSource`) and/or **WebXR depth-sensing**. This is the
  one piece gated on the live IWSDK renderer API.
- Keep `invertY` configurable — depends on the capture pipeline's flip.

---

## Rendering & drawing techniques (recreate)

| Technique | Source | Recreation |
|-----------|--------|-----------|
| **Hollow outline box** | `SentisInferenceUiManager.CreateNewBox`: sliced sprite, `fillCenter=false` | CSS border box (DOM overlay) or an unfilled quad/line-loop in WebGL. |
| **Pooled boxes** (reuse via SetActive, no per-frame GC) | `m_boxPool`, `DrawBox` | Reuse DOM nodes / mesh instances keyed by index; hide surplus. Important on-device. |
| **Label billboard to camera** | `DrawBox`: `LookRotation(pos - capturedCameraPos)`; `DetectionTagController`/`DetectionSpawnMarkerAnim`: `LookAt(centerEye)` | Billboard the label entity toward the XR camera each frame. |
| **Spin animation on marker** | `DetectionTagController`, `DetectionSpawnMarkerAnim` (angular speed per axis) | Cosmetic; optional. |
| **Sequential spawn-in** (fade/scale/slide, `EaseOutCubic`, staggered) | `SequentialSpawnAnimator` | Optional UX polish when a batch of detections appears. |

---

## Canvas-at-camera-pose + pose freeze (recreate — solves latency drift)

`SentisObjectDetectedUiManager` + `SentisInferenceUiManager`:
- Scales the overlay canvas so it **exactly matches the camera horizontal FoV** at
  a chosen distance: FoV from `ScreenPointToRayInCamera` on the left/right edge
  pixels, then `width = 2·d·tan(FoV/2)`.
- **`CapturePosition()` freezes** the canvas at the camera pose **when the frame
  was captured**; `UpdatePosition()` re-applies it when boxes are drawn.

Why it matters: server detections arrive **after** a network round-trip, so the
head has moved. Anchoring boxes to the *capture* pose (not the current pose) keeps
them aligned with the pixels they describe. **The recreation should snapshot the
XR view/camera pose at frame-capture time and render that detection batch against
the snapshotted pose** (WebXR: cache the `XRFrame` view matrix / an anchor).

---

## Deduplication policy (recreate — configurable)

Two variants in the reference:
- **One-per-class:** `_spawnedClasses` HashSet (`allowMultiplePerClass=false`).
- **Spatial + class:** skip if an existing marker of the same class is within
  `minDistanceMeters` / `m_spawnDistance` (`DetectionManager.PlaceMarkerUsingEnvironmentRaycast`,
  `DetectionSpawnerManager.ShouldSkipDetection`).

Recreate as a pluggable dedup policy (none / per-class / spatial-per-class).

---

## Lifecycle & UX flow (recreate the relevant parts)

| Behaviour | Source | WebXR equivalent |
|-----------|--------|------------------|
| **Permission → set resolution from intrinsics → enable capture** | `SentisObjectDetectedUiManager.Start`, `CameraViewerManager` | `getUserMedia` permission gate before starting the track; pick capture resolution. |
| **Commit-on-input** (live overlay is transient; press A to place permanent markers; play sound; dedup) | `DetectionManager` | Controller/`select` event to commit anchors; audio cue. Optional but good UX. |
| **Pause gating + cooldown** after menu | `DetectionManager` (`m_isPaused`, `m_delayPauseBackTime`) | Pause inference/placement when a menu is open. |
| **Recenter cleanup** — destroy placed markers when tracking space recenters | `OVRManager.display.RecenteredPose` callbacks | WebXR reference-space **`reset`** event → clear anchors. |
| **Snapshot / freeze frame** for inspection | `CameraToWorldManager` (button toggles `WebCamTexture.Stop/Play`) | Pause the track + freeze overlay; debug aid. |

---

## Image qualifiers (recreate — ties to the server "qualify images" goal)

`BrightnessEstimationManager` computes **Rec.709 luminance**
(`0.2126·R + 0.7152·G + 0.0722·B`) averaged over the frame, smoothed via a ring
buffer, throttled by a refresh interval, emitted as a `UnityEvent<float>`.

**Value:** a cheap **client-side quality gate** — e.g. skip sending frames when
the scene is too dark (detections would be unreliable), or surface a "low light"
hint. Directly supports the server's stated aim of *validating and qualifying*
images, but done at the edge to save bandwidth/inference. Recreate in TS over a
downscaled frame (or a WebGL reduction). Candidate additional qualifiers: motion
/ blur (frame-to-frame delta), over/under-exposure histogram.

---

## Performance techniques worth carrying over

- **Send-every-N-frame throttle** + GPU RGB→YUV conversion (`PCAVideoStreamer`,
  `RGBtoYUV420.compute`). WebRTC in the browser handles encoding, but keep a
  capture-rate throttle and a downscale before send.
- **Box pooling** (above) — avoid per-frame allocation in the overlay.
- **Layer-by-layer inference budget** (`SentisInferenceRunManager`: N layers/frame
  + async readback state machine) — only relevant **if** the recreation ever adds
  an **on-device** fallback (WebGPU / `transformers.js`) instead of the server
  round-trip. Noted as an option, not required.
- **Adaptive resolution awareness** — the client already trusts the server's
  per-frame `width/height`; the server ramps low→high. Recreation must honour it
  in the normalize step, not hardcode 640×480.

---

## Priority for the recreation

1. **P0** — bbox→world placement math (+ per-frame stream dims, invertY) and the
   detection wire types.
2. **P0** — a `DetectionRenderer` seam with the world-anchored-tag default;
   dedup policy; pose-freeze for latency alignment.
3. **P1** — 2D outline-box overlay option, pooling, billboard labels.
4. **P1** — brightness qualifier as a client-side gate.
5. **P2** — commit-on-input flow, recenter cleanup, spawn-in animation, snapshot
   debug mode, frustum/ray debug viz.

The renderer's world-placement (hit-test/depth) is the only part gated on the
live IWSDK API; everything else can be built from the wire protocol + these
references.
