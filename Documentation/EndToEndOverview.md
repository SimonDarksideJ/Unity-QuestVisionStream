# QuestVisionStream: End-to-End Architecture Overview

## Documentation Navigation

- [Architecture & Diagrams](ArchitectureDiagram.md)
- [Unity Client Component](UnityClient.md)
- [Server Component](QuestVisionStreamServer.md)
- [Message Transmission & Schemas](MessageTransmissionGuide.md)
- (You are here) End-to-End Overview

## Solution Summary

QuestVisionStream enables real-time computer vision on Meta Quest headsets by streaming camera frames from Unity to an external GPU server for AI inference (e.g., YOLO), then returning detection results for spatial positioning in Unity. The system leverages WebRTC for low-latency video and data channel communication.

## High-Level Architecture

```text
Meta Quest (Unity) <---WebRTC---> QuestVisionStreamServer (Python)
   |                                      |
   |--Passthrough Camera API (PCA)         |--AI Models (YOLO, Florence2, etc.)
   |--Unity App (PCAVideoStreamer)         |--Video Processing
   |--Native Android Plugin                |--WebRTC Signaling/Data Channel
   |--FrameSender (YUV conversion)         |--Detection Results
   |--QuestVisionStreamBridge              |
```

## Component Interactions

1. **Unity Client**
   - Captures camera frames using PCA.
   - Converts frames to YUV (GPU compute shader).
   - Streams video via WebRTC to the server.
   - Receives detection results via WebRTC data channel.
   - Positions objects in Unity based on detection coordinates.

2. **QuestVisionStreamServer**
   - Accepts WebRTC video stream.
   - Processes frames (pre-processing: flip, rotate, etc.).
   - Runs AI inference (YOLO, Florence2, etc.).
   - Sends detection results (bounding boxes, labels, coordinates) back to Unity via data channel.

## API/Data Flow

1. **Connection Negotiation**
   - Unity connects to server using secure WebSocket (wss://) for signaling.
   - ICE/STUN/TURN servers configured for NAT traversal.

2. **Image Shipping**
   - Unity streams YUV frames over WebRTC video track.
   - Server receives and processes frames.

3. **Feature Detection**
   - Server runs selected AI model on each frame.
   - Results include object labels, bounding boxes, and confidence scores.

4. **Coordinate Shipping**
   - Server sends detection results as JSON over WebRTC data channel.
   - Unity receives and parses results, spawning/interacting with objects in the scene.

## Message Schemas

- **Detection Result (JSON)**

  ```json
  {
    "frame_id": 123,
    "detections": [
      {
        "label": "person",
        "confidence": 0.98,
        "bbox": [x, y, width, height],
        "center": [cx, cy]
      },
      ...
    ]
  }
  ```

## Technologies Used

- Unity (C#)
- Native Android Plugin (Java)
- Python (aiortc, OpenCV, YOLO)
- WebRTC (video + data channel)

---
See: [Unity Client](UnityClient.md) | [Server Component](QuestVisionStreamServer.md) | [Message Transmission Guide](MessageTransmissionGuide.md) | [Architecture Diagrams](ArchitectureDiagram.md)

## Spatial Placement & Coordinate Conversion

This section explains how 2D detector outputs (YOLO-style bounding boxes) are converted into 3D marker/admonition placement in the Unity scene.

### 1. Coordinate Spaces
- Detector space: Pixel coordinates (origin top-left) width=W, height=H.
- Normalized UV: (u,v) in [0,1].
- NDC (clip space pre-projection): x_ndc, y_ndc in [-1,1].
- View space (camera local): Forward +Z (Unity default for Camera) after inverse projection.
- World space: Final marker position / orientation.

### 2. Pre-processing Corrections

Server may flip or rotate frames before detection:

- Vertical flip (`FLIP_VERTICAL=True`): incoming detection y must be inverted: y' = H - 1 - y.
- Horizontal flip (`FLIP_HORIZONTAL=True`): x' = W - 1 - x.
- 180° rotation (`ROTATE_180=True`): apply both flips AND rotate bbox orientation if needed.

If the client applies its own mirroring (e.g., passthrough feed displayed unflipped), ensure only one correction path is applied.

### 3. Pixel -> Normalized -> NDC

Given bbox center (cx_px, cy_px):

```text
u = cx_px / W
v = cy_px / H
x_ndc = u * 2 - 1
// Note: YOLO y grows downward; Unity NDC y grows upward
y_ndc = 1 - v * 2
```

### 4. Reconstruct View Ray

```text
clip = (x_ndc, y_ndc, 1, 1)
view = InverseProjection * clip
Direction_view = normalize(view.xyz / view.w)
```

### 5. Transform to World

```text
Direction_world = CameraTransform.TransformDirection(Direction_view)
Origin_world = CameraTransform.position
```

### 6. Depth Resolution / Placement Strategies

1. Raycast world (Physics.Raycast) along Direction_world – place marker at hit point.
2. Fixed depth billboard: position = Origin_world + Direction_world * defaultDepth (e.g., 2.0m).
3. Depth texture sampling (if available) – sample XR depth / reconstructed mesh to refine distance.

### 7. Bounding Box Corners (Optional for Framing)

Project each corner (x_min,y_min), (x_max,y_min), (x_max,y_max), (x_min,y_max) through same pipeline to get a quad in world space. Useful for drawing 3D frames.

### 8. Handling Aspect / Letterboxing

If the streamed frame is a cropped or scaled variant of the passthrough display:

- Maintain the capture resolution (W,H) used for detection.
- Apply offset & scale: `u = (cx_px + padX) / effectiveWidth` etc.

### 9. Latency & Smoothing

- Apply temporal smoothing (lerp) of marker position over a few frames to reduce jitter.
- Keep last N detections indexed by label/id, update only when IOU threshold exceeded.

### 10. Sample Pseudocode

```csharp
Vector3 GetWorldPoint(int cx, int cy, int w, int h, Camera cam, float fallbackDepth)
{
   // Adjust for server flips (example: vertical flip only)
   if (serverFlipVertical) cy = h - 1 - cy;
   if (serverFlipHorizontal) cx = w - 1 - cx;

   float u = (float)cx / w;
   float v = (float)cy / h;
   float xNdc = u * 2f - 1f;
   float yNdc = 1f - v * 2f;

   var clip = new Vector4(xNdc, yNdc, 1f, 1f);
   var invProj = cam.projectionMatrix.inverse;
   var view = invProj * clip;
   var dirView = new Vector3(view.x, view.y, view.z).normalized;
   var worldDir = cam.transform.TransformDirection(dirView);

   if (Physics.Raycast(cam.transform.position, worldDir, out var hit, 20f))
      return hit.point;
   return cam.transform.position + worldDir * fallbackDepth;
}
```

### 11. Marker Orientation

- Face user: `marker.transform.forward = (marker.transform.position - cam.position).normalized;`
- Or align with surface normal when using Raycast hit.

### 12. Scaling

- Scale by distance * tan(fov/constant) to keep consistent screen-space size.

### 13. Synchronizing Frame IDs

Use `frame_id` from detection payload to discard late/out-of-order results if Unity has advanced frames.

### Assumptions

Where the current implementation does not expose camera intrinsics, a projection-based ray build is used. If intrinsics become available, replace with an inverse intrinsic matrix multiply for improved accuracy.
