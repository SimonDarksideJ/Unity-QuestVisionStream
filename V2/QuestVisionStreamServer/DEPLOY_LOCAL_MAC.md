# Deploy: native macOS (Mac Mini M2)

The recommended host — lowest latency, MPS GPU, and LAN-direct WebRTC media.

## Why native (not Docker) on macOS

- Docker Desktop on macOS has **no MPS GPU passthrough** (CPU-only inference).
- Docker Desktop on macOS has **no `--network host`**, so WebRTC media from inside
  a container usually needs a TURN relay — adding latency.
- Native Python gets the MPS GPU and, on the LAN, media flows headset↔Mac directly.

## Steps

```bash
cd QuestVisionStreamServer
./run-local.sh          # creates .venv, installs base + YOLO, sets MPS env, runs
```

`run-local.sh` exports the Apple-Silicon environment:
- `PYTORCH_ENABLE_MPS_FALLBACK=1` — CPU fallback for ops MPS lacks.
- `PYTORCH_MPS_HIGH_WATERMARK_RATIO=0.0` — allow full unified-memory use.

## Networking

- **On-LAN (default):** `QVS_ENABLE_TURN=false`. Point the client at
  `ws://<mac-lan-ip>:3000`. Media flows direct.
- **Remote/online:** put a reverse proxy in front for WSS signaling
  (`deploy/Caddyfile.example`) **and** enable TURN
  (`QVS_ENABLE_TURN=true` + `QVS_TURN_*`) so media can cross NATs.

## Performance levers

- `QVS_YOLO_IMGSZ=480` (or lower) — biggest latency win.
- `QVS_YOLO_HALF=true` — FP16 on MPS.
- Consider exporting the model to CoreML/ONNX for the M2 (future enhancement).
