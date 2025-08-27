# Unity Client Component

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
