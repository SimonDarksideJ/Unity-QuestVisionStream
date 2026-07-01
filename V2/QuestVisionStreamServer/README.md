# QuestVisionStreamServer (V2)

Modernized, self-hostable inference server. Receives a WebRTC camera track from a
client (Unity or the WebXR Quest client), runs a detector, and streams detections
back over the `detections` data channel.

## What changed from V1

| V1 issue | V2 |
|----------|-----|
| CLI `--detector` choices drifted from the registry (`owl2`≠`owlv2`, `grounding_dino` unreachable) | Choices derive from the registry (`DETECTOR_NAMES`) — cannot drift |
| Not headless-safe (`cv2.imshow` unconditional, display on by default) | Headless by default; all display guarded and auto-disables under `opencv-python-headless` |
| Hardcoded config + baked-in TURN creds | Fully env-driven (`QVS_*`), configurable STUN/TURN |
| Detector drew boxes every frame on the hot path | Detectors return detections only; drawing happens only when displaying |
| Unpinned, monolithic deps (torch+transformers+mediapipe always) | Pinned base + per-detector extras |
| Body detector pushed an ndarray over the data channel | Body returns a proper bbox detection (wire-format consistent) |
| No health endpoint; FPS math wrong | `/` health endpoint; corrected FPS window |

## Layout

```
questvisionstream/
├── server.py          # entry: python -m questvisionstream
├── config.py          # QVS_* environment configuration
├── webrtc_server.py   # aiortc peer connection + websockets signaling
├── video_processor.py # frame loop → detector → detections payload
├── health.py          # dependency-free HTTP health endpoint
└── detectors/         # registry (single source of truth) + implementations
```

## Run

```bash
# Native (recommended on Apple Silicon — MPS GPU, LAN-direct media):
./run-local.sh                       # installs deps for QVS_DETECTOR (default yolo)

# Or manually:
pip install -r requirements.txt -r requirements-yolo.txt
python -m questvisionstream --detector yolo

# Docker (CPU fallback, Linux):
docker compose up --build
```

Health: `curl http://localhost:8080/` → `{"status":"ok","detector":"yolo","connections":0}`

## Configuration (`QVS_*`)

| Var | Default | Purpose |
|-----|---------|---------|
| `QVS_HOST` / `QVS_PORT` | `0.0.0.0` / `3000` | Signaling bind |
| `QVS_HEALTH_PORT` | `8080` | Health endpoint |
| `QVS_DETECTOR` | `yolo` | `yolo`\|`florence2`\|`owlv2`\|`grounding_dino`\|`body` |
| `QVS_ENABLE_DISPLAY` | `false` | Local debug window |
| `QVS_FLIP_VERTICAL` / `_HORIZONTAL` / `QVS_ROTATE_180` | `true` / `false` / `false` | Frame pre-processing |
| `QVS_STUN_URLS` | Google STUN | Comma-separated STUN URLs |
| `QVS_ENABLE_TURN` | `false` | Enable TURN (remote/NAT) |
| `QVS_TURN_URLS` / `_USERNAME` / `_CREDENTIAL` | — | TURN relay |
| `QVS_YOLO_IMGSZ` / `QVS_YOLO_HALF` / `QVS_YOLO_CONF` | `640` / `false` / `0.6` | YOLO latency levers |
| `QVS_YOLO_IGNORE` | people/vehicles | Classes to drop |

See `DEPLOY_LOCAL_MAC.md` and `DEPLOY_HF_SPACES.md` for host-specific guidance.

## Protocol

Detections are pushed as JSON over the client-created `detections` data channel:

```json
{ "type": "detections", "frame": 123, "width": 640, "height": 480,
  "detections": [ { "label": "cup", "conf": 0.82, "bbox": [x1, y1, x2, y2] } ] }
```

Byte-identical to the Unity `DetectionsPayload` and the `@questvisionstream/client`
wire types — one server serves both clients unchanged.
