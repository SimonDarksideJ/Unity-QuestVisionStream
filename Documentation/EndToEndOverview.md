# QuestVisionStream: End-to-End Architecture Overview

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
See individual component documents for further details.
