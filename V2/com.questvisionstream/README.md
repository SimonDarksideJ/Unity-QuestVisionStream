# @questvisionstream/client

> 📖 Full docs hub: [`../Documentation/`](../Documentation/README.md).

Reusable, **host-agnostic** WebRTC streaming client for QuestVisionStream, built
as a graph of [RealityCollective Service Framework](../service-framework)
services. It speaks the exact protocol of `QuestVisionStreamServer` and exposes a
framework-agnostic **detection rendering seam**, so the same library drives the
IWSDK Quest client, a plain browser overlay, or a test double — unchanged.

This is the `com.questvisionstream` library folder. It is **file-linked** into
the Quest client (`file:../com.questvisionstream`) rather than copied, so the
streaming implementation stays in one reusable place.

## Service graph

Each concern is a service, resolved by interface token, orchestrated by the
`ServiceManager` in priority order:

| Priority | Service (token) | Responsibility |
|----------|-----------------|----------------|
| 10 | `ISignalingService` | WebSocket transport: `offer`/`answer`/`candidate` JSON, reconnect |
| 15 | `IImageQualifierService` | Edge quality gate; drives analyzer **modules** (brightness…) |
| 20 | `IWebRTCService` | `RTCPeerConnection`, `detections` data channel, camera track, ICE `candidate:` prefix fix |
| 30 | `IDetectionService` | Parse/validate payloads, track adaptive frame size + throughput |

The `IImageQualifierService` composes **service modules** (data providers) —
`BrightnessQualifierModule` (Rec.709 luminance, ported from the Unity
`BrightnessEstimationManager`) — that each score one property of a frame.

## Protocol fidelity

- Wire types (`Detection`, `DetectionsPayload`) are byte-identical to the server
  and the Unity `DetectionsPayload`/`Detection`, so one server serves both clients.
- The **client** creates the `detections` `RTCDataChannel`; the server only
  listens. The client is the offerer and adds the camera video track.
- aiortc carries ICE candidate lines **without** the `candidate:` SDP prefix;
  `WebRTCService` strips it on send and re-adds it on receive.

## Usage

```ts
import { QuestVisionStreamClient, toRenderBatch } from '@questvisionstream/client';

const qvs = new QuestVisionStreamClient({
  signalingUrl: 'ws://192.168.1.20:3000',
  // iceServers: [{ urls: 'turn:...', username, credential }], // remote only
});

await qvs.start();                 // opens signaling, brings services up
await qvs.connect(cameraStream);   // MediaStream from getUserMedia / IWSDK CameraSource

qvs.onDetections((payload) => {
  const batch = toRenderBatch(payload, { invertY: true });
  myRenderer.renderDetections(batch); // myRenderer implements IDetectionRenderer
});

// Optional edge gate: only stream when the image is good enough
qvs.setQualifierFrameProvider(() => downsampledCameraFrame());

// Each frame from the host loop:
qvs.update(deltaSeconds);
```

## Rendering seam

The library computes resolution-independent `RenderBatch`es (normalized viewport
centers + rects) via `toRenderBatch` / `normalizeDetection`, and provides a
`DetectionDeduper` (`per-class` or `spatial-per-class`, ported from the Unity
client). The **host** implements `IDetectionRenderer.renderDetections()` to place
world-anchored tags (IWSDK hit-test/unprojection) or draw 2D overlay boxes.

## Build

```bash
npm run typecheck   # tsc --noEmit
npm run build       # emits dist/
```

Depends on `@realitycollective/service-framework-ts` (sibling, file-linked).
