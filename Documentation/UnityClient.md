# Unity Client Component

## Documentation Navigation

- [End-to-End Overview](EndToEndOverview.md)
- [Architecture & Diagrams](ArchitectureDiagram.md)
- [Server Component](QuestVisionStreamServer.md)
- [Message Transmission & Schemas](MessageTransmissionGuide.md)
- (You are here) Unity Client Component

## Overview

The Unity client is responsible for capturing camera frames, streaming them to the server, and handling detection results for spatial operations.

## Key Classes

- **PCAVideoStreamer**: Manages camera capture, WebRTC configuration, and frame streaming.
- **QuestVisionStreamBridge**: Handles native plugin calls for WebRTC, frame data, and message passing.
- **FrameSender**: Converts frames to YUV using GPU compute shaders and dispatches them for streaming.
- **QuestVisionStreamEvents**: UnityEvents for connection, video, and detection result handling.

## Workflow

1. **Initialization**
   - Configure signaling server URL, ICE/STUN/TURN servers.
   - Set target FPS and resolution.
   - Connect to signaling server via WebRTC.

2. **Frame Capture & Shipping**
   - Capture frames from PCA camera.
   - Convert to YUV format (GPU optimized).
   - Send frames via WebRTC video track using native plugin.

3. **Receiving Detection Results**
   - Listen for JSON messages on data channel.
   - Parse detection results and trigger `OnDetections` event.
   - Use detection data for object spawning, positioning, or interaction in Unity.

## Example Event Handling

```csharp
public void OnDetections(string json)
{
    // Parse JSON and use detection data
    onDetectionsJson?.Invoke(json);
}
```

## Configuration

- **Signaling Server URL**: Must use `wss://` for Android WebRTC.
- **ICE Servers**: STUN/TURN for NAT traversal.
- **Frame Rate/Resolution**: Adjustable for performance/quality.

## References

- `PCAVideoStreamer.cs`
- `QuestVisionStreamBridge.cs`
- `FrameSender.cs`
- `QuestVisionStreamEvents.cs`

Related docs: [Message Transmission Guide](MessageTransmissionGuide.md) | [Server Component](QuestVisionStreamServer.md)

## Spatial Positioning & Marker Placement

This section details how detection bounding boxes are translated into 3D admonitions/markers in the headset view.

### Pipeline Summary

1. Receive detection JSON with pixel-space bbox (x,y,width,height) and/or center.
2. Adjust for server-side image transforms (flip / rotate) to recover original camera orientation.
3. Convert pixel to normalized UV (u,v) relative to streamed frame dimensions.
4. Convert to NDC ([-1,1]) adjusting Y inversion.
5. Reconstruct view-space direction via inverse projection matrix.
6. Transform to world space; obtain placement point via raycast or fixed depth.
7. Orient billboard marker to face headset or align to surface normal.
8. Smooth position across frames for stability.

### Flip / Rotation Handling

```text
if (flipVertical)  y = H - 1 - y;
if (flipHorizontal) x = W - 1 - x;
if (rotate180) { x = W - 1 - x; y = H - 1 - y; }
```

### Pixel -> NDC Conversion

```text
u = x / W
v = y / H
x_ndc = u * 2 - 1
y_ndc = 1 - v * 2 // invert vertical
```

### View Ray Reconstruction (C#)

```csharp
Vector3 ComputeDirection(int px, int py, int W, int H, Camera cam)
{
   float u = (float)px / W;
   float v = (float)py / H;
   float xNdc = u * 2f - 1f;
   float yNdc = 1f - v * 2f;
   var clip = new Vector4(xNdc, yNdc, 1f, 1f);
   var view = cam.projectionMatrix.inverse * clip;
   var dirView = new Vector3(view.x, view.y, view.z).normalized;
   return cam.transform.TransformDirection(dirView);
}
```

### Placement Strategies

- Raycast world & place at hit
- Fixed distance (e.g., 2m) when no geometry
- Depth/mesh sampling (future extension)

### Marker Orientation & Scale

```csharp
marker.forward = (marker.position - cam.transform.position).normalized; // face user
marker.localScale = Vector3.one * (distance * scaleFactor);
```

### Handling Multiple Detections

Maintain a dictionary keyed by label or stable hash (label + proximity). Update existing marker when IOU > threshold; create new otherwise.

### Temporal Smoothing

```text
marker.position = Vector3.Lerp(marker.position, targetPos, smoothingFactor * Time.deltaTime);
```

### Frame Consistency

Discard detection if `detection.frame_id < lastRenderedFrameId` to avoid late arrivals.

### Bounding Box Corners (Optional Frustum Quad)

Project each corner using the same transformation for drawing wireframe rectangles in 3D.

### Assumptions

Current code path does not expose intrinsic calibration; inverse projection approximation is used. If intrinsics become available: `dir_cam = normalize(K^{-1} * [x; y; 1])`.

### Future Enhancements

- Depth estimation for accurate z placement
- Per-class custom mesh spawning
- Confidence-based fading / scaling
