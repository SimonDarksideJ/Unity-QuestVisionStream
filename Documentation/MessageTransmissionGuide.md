# Message Transmission & Content Guide

## Overview

This guide details the schemas, formats, and protocols for all messages exchanged between Unity and QuestVisionStreamServer.

## Connection & Negotiation

- **Signaling**: Unity connects to server using secure WebSocket (`wss://`).
- **ICE/STUN/TURN**: Configured for NAT traversal.
- **WebRTC**: Used for both video streaming and data channel communication.

## Image Shipping

- **Format**: YUV (optimized for GPU and WebRTC).
- **Transport**: WebRTC video track.
- **Unity Side**: Uses `FrameSender` and `QuestVisionStreamBridge` to send frames.
- **Server Side**: Receives frames, converts to OpenCV format for processing.

## Detection Results (Data Channel)

- **Format**: JSON
- **Transport**: WebRTC data channel
- **Schema**:

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

### Field Details

- `frame_id`: Integer, unique per frame.
- `detections`: Array of detection objects.
  - `label`: String, class name (e.g., "person").
  - `confidence`: Float, model confidence (0-1).
  - `bbox`: Array `[x, y, width, height]` (pixel coordinates).
  - `center`: Array `[cx, cy]` (pixel coordinates).

## Unity Event Handling

- **OnDetections**: Receives JSON string, parses, and uses for spatial operations.

## Example Transmission

- **Unity → Server**: YUV frame (WebRTC video track)
- **Server → Unity**: Detection JSON (WebRTC data channel)

## References

- `QuestVisionStreamBridge.cs`
- `FrameSender.cs`
- `server.py`
- `video_processor.py`
