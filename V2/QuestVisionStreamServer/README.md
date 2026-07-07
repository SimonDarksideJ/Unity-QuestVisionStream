# QuestVisionStreamServer (V2)

> 📖 Full docs hub: [`../Documentation/`](../Documentation/README.md).

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
| `QVS_YOLO_IMGSZ` / `QVS_YOLO_HALF` / `QVS_YOLO_CONF` | `640` / `false` / `0.6` | YOLO latency levers (`HALF` is CUDA-only; refused elsewhere) |
| `QVS_YOLO_IGNORE` | people/vehicles | Classes to drop |
| `QVS_AUTH_TOKEN` | *(unset)* | When set, clients must dial `ws(s)://host:3000/?token=<value>` |
| `QVS_ALLOWED_ORIGINS` | *(unset = any)* | Comma-separated `Origin` allowlist for the signaling WS |
| `QVS_MAX_CONNECTIONS` | `1` | Session cap; a new connection supersedes the oldest |
| `QVS_LOG_INTERVAL` | `30` | Frames between FPS log lines (clamped ≥ 1) |
| `QVS_DETECTION_LOG` | *(unset = off; launch scripts default it)* | Path to a JSONL detection log — one line per payload sent to the client, for client-side comparison |

> **Exposing the server beyond the LAN?** Set `QVS_AUTH_TOKEN` (append
> `?token=…` to the signaling URL the client dials) and `QVS_ALLOWED_ORIGINS`
> (e.g. `https://questvisionstream.pages.dev`). Both default open for
> trusted-LAN use. `QVS_MAX_CONNECTIONS` defaults to 1 because the detector
> model (and its state, for `florence2`/`body`) is shared across connections —
> the newest connection wins, so a lingering half-open session never blocks a
> reconnecting headset.

Host-specific guides live in the docs hub:
[Install-Mac-M2](../Documentation/Install-Mac-M2.md) (native Apple Silicon + remote
access) and [Deploy-HuggingFace-Spaces](../Documentation/Deploy-HuggingFace-Spaces.md)
(free cloud GPU). Full index: [`../Documentation/`](../Documentation/README.md).

## Protocol

Detections are pushed as JSON over the client-created `detections` data channel:

```json
{ "type": "detections", "frame": 123, "pts": 369000, "width": 640, "height": 480,
  "detections": [ { "label": "cup", "conf": 0.82, "bbox": [x1, y1, x2, y2] } ] }
```

Compatible with the Unity `DetectionsPayload` and the `@questvisionstream/client`
wire types — one server serves both clients unchanged. `pts` (added in the 2026-07
hardening pass) is the media timestamp of the processed frame in RTP clock units
(90 kHz), or `null` when the source frame carries none; existing clients ignore
it, and future clients can use it to correlate detections with the exact captured
frame (the groundwork for capture-pose alignment). `frame` counts frames
*received*, so under load its gaps show how many stale frames were skipped by the
latest-frame-wins scheduler.

### Detection log (server-side, for client comparison)

Set `QVS_DETECTION_LOG` (the launch scripts default it to `.run/detections.jsonl`
beside `server.log`) and the server appends **one JSON line per payload it sends
to a client** — the machine-readable counterpart to the throttled `[QVS ↓]`
console summary. Each line wraps the exact wire payload plus send-side metadata:

```json
{"ts":"2026-07-06T18:57:01.123456+00:00","t_mono":950247.48,
 "client":"user@example.com","count":1,
 "payload":{"type":"detections","frame":123,"pts":369000,"width":640,"height":480,
            "detections":[{"label":"cup","conf":0.82,"bbox":[x1,y1,x2,y2]}]}}
```

`payload` is byte-faithful to what the client receives, so a client-side capture
can be diffed against it on `frame`/`pts`. The file is truncated per server run
(like `server.log`); lines are logged only on a successful data-channel send, so
it mirrors the wire rather than intent. `tail -f` shows detections live.

## Behaviour under load

The frame pipeline (see `video_processor.py`) keeps two guarantees, both covered
by tests:

- **Inference never blocks the event loop** — preprocess + detect run on a
  single worker thread, so signaling, keepalives, and the health endpoint stay
  responsive during a slow forward pass.
- **Latest-frame-wins** — a reader task keeps draining the track while inference
  runs; stale frames are dropped so detections track the live camera instead of
  drifting seconds behind. The FPS log line reports received/processed/dropped.

## Tests

```bash
pip install -r requirements.txt -r requirements-dev.txt
python -m pytest tests/ -c tests/pytest.ini --rootdir=.
```

The suite runs without any model weights or GPU — detectors are stubbed and the
WebRTC layer is driven through fakes. The two performance tests print the
measured event-loop stall and end-to-end lag; the improvement history with
before/after numbers lives in
[`../Documentation/improvements/`](../Documentation/improvements/README.md).

CI runs this suite (plus the TypeScript typecheck gates) on every PR/push that
touches `V2/` — see `.github/workflows/v2-tests.yml`; the measured stats are
published to each run's job summary.
