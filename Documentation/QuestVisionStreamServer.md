# QuestVisionStreamServer Component

## Documentation Navigation

- [End-to-End Overview](EndToEndOverview.md)
- [Architecture & Diagrams](ArchitectureDiagram.md)
- [Unity Client Component](UnityClient.md)
- [Message Transmission & Schemas](MessageTransmissionGuide.md)
- (You are here) Server Component

## Overview

The server receives video frames from Unity, processes them, runs AI inference, and returns detection results via WebRTC data channel.

## Key Modules

- **server.py**: Entry point, manages WebRTC signaling and data channel.
- **video_processor.py**: Handles frame pre-processing (flip, rotate, etc.).
- **webrtc_server.py**: Manages WebRTC peer connection, tracks, and data channels.
- **detectors/**: Contains AI model implementations (YOLO, Florence2, etc.).
- **config.py**: Server and image processing configuration.

## Workflow

1. **Startup & Configuration**
   - Select detector (YOLO, Florence2, etc.) via command-line argument.
   - Configure image pre-processing (flip, rotate) and server settings.

2. **WebRTC Negotiation**
   - Accepts signaling from Unity client (wss://).
   - Configures ICE/STUN/TURN servers for connection.

3. **Frame Reception & Processing**
   - Receives video frames over WebRTC video track.
   - Processes frames (OpenCV: flip, rotate, etc.).
   - Passes frames to selected detector for inference.

4. **Feature Detection**
   - Detector returns list of detections (label, confidence, bounding box, center).
   - Results are formatted as JSON.

5. **Coordinate Shipping**
   - Sends detection results over WebRTC data channel to Unity client.

## Example Detection Result

```json
{
  "frame_id": 123,
  "detections": [
    {
      "label": "person",
      "confidence": 0.98,
      "bbox": [x, y, width, height],
      "center": [cx, cy]
    }
  ]
}
```

## Configuration Options

- **Image Pre-processing**: `FLIP_VERTICAL`, `FLIP_HORIZONTAL`, `ROTATE_180`
- **Server Settings**: `HOST`, `PORT`, `STUN_SERVER`
- **Detector Selection**: `--detector yolo|florence2|owl2|body`

## References

- `server.py`
- `video_processor.py`
- `webrtc_server.py`
- `detectors/`
- `config.py`

Related docs: [Message Transmission Guide](MessageTransmissionGuide.md) | [Unity Client](UnityClient.md) | [End-to-End Overview](EndToEndOverview.md)
