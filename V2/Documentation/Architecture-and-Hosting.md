# Architecture & Hosting Guide

The big picture of QuestVisionStream V2: what the components are, how data flows,
and where each piece can run.

Related: [Configuration-and-Connectivity.md](Configuration-and-Connectivity.md)
(how the client finds + connects to the server),
[Install-Mac-M2.md](Install-Mac-M2.md) (server hosting on Apple Silicon),
[Deployment-Cloudflare-Pages.md](Deployment-Cloudflare-Pages.md) (client hosting),
and `../README.md` (repo layout).

---

## 1. System overview

```
      ┌──────────────────────────── HEADSET (Meta Quest, Horizon Browser) ────────────────────────────┐
      │  quest-client  (Meta IWSDK · Three.js · ECS)                                                   │
      │  ┌────────────────────────────────────────────────────────────────────────────────────────┐   │
      │  │  IWSDK Systems                        Service Framework (DI, ServiceManager)             │   │
      │  │  • ServicePumpSystem  ──drives──▶  ISignalingService → IImageQualifierService            │   │
      │  │  • CameraStreamSystem  (CameraSource → MediaStream)   → IWebRTCService → IDetectionService│  │
      │  │  • DetectionRenderSystem (IDetectionRenderer: world-anchored tags)                        │  │
      │  └────────────────────────────────────────────────────────────────────────────────────────┘   │
      └───────────────────────────────────────────────┬────────────────────────────────────────────────┘
                                    WebRTC: camera video ▲   │ ▼ detections DataChannel
                                    WebSocket signaling  ◀───┼───▶  (ws/wss :3000)
      ┌───────────────────────────────────────────────┴────────────────────────────────────────────────┐
      │  QuestVisionStreamServer  (Python · aiortc · asyncio)                                             │
      │  webrtc_server → VideoProcessor → detector (YOLO / OWLv2 / GroundingDINO / Florence-2 / Body)     │
      │  health :8080         device: MPS (Apple) / CUDA / CPU                                            │
      └──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Two independently-hostable halves — a **static client** and a **Python inference
server** — joined by **WebRTC** (media) + a **WebSocket** (signaling).

---

## 2. Components

### Client side (all TypeScript, in `V2/`)

| Package | Role |
|---------|------|
| **`quest-client/`** | The IWSDK WebXR app — the *host*. Thin: creates the ECS world, owns the camera, renders detections as world-anchored tags. Deployed to Cloudflare Pages. |
| **`com.questvisionstream/`** | Reusable, host-agnostic streaming **library** (`@questvisionstream/client`). All the WebRTC/signaling/detection/qualifier logic, as Service Framework services. **File-linked** into the client (reusable, single source). |
| **`service-framework/`** | TypeScript port of the RealityCollective Service Framework — the DI/lifecycle backbone (`ServiceManager`, `IService`, service modules). Host-agnostic (no IWSDK/Three/DOM deps). |

The client wires these with **dependency injection**: systems resolve services by
interface token (`getService(IWebRTCService)`) from the `ServiceManager`; they
never import concrete service classes. Swap the host (IWSDK → a DOM overlay →
tests) and reuse the library unchanged.

### Server side (`V2/QuestVisionStreamServer/`, Python)

| Module | Role |
|--------|------|
| `webrtc_server.py` | aiortc peer connection + websockets signaling |
| `video_processor.py` | frame loop → detector → detections payload (headless) |
| `detectors/` | registry (single source of truth) + YOLO/OWLv2/GroundingDINO/Florence-2/Body |
| `config.py` | env-driven (`QVS_*`) configuration |
| `health.py` | dependency-free HTTP health endpoint |

---

## 3. End-to-end data flow

1. **Capture** — `CameraStreamSystem` creates an IWSDK `CameraSource`; when Active
   it hands the `MediaStream` to `IWebRTCService`.
2. **Qualify (edge gate)** — `IImageQualifierService` samples downscaled frames
   (Rec.709 luminance); if too dark/over-exposed it toggles the outbound track off,
   saving bandwidth + server inference.
3. **Stream** — `IWebRTCService` (client = offerer) sends the camera video track;
   the server's `VideoProcessor` pulls frames.
4. **Detect** — the selected detector returns `{label, conf, bbox}` per frame; the
   server pushes them over the client-created `detections` DataChannel.
5. **Parse** — `IDetectionService` validates payloads, tracks the (adaptive) frame
   size, republishes typed detections.
6. **Render** — `DetectionRenderSystem` normalizes each bbox (resolution-independent),
   unprojects it through the XR camera, and places a world-anchored tag.

Wire format (identical to the Unity client, so one server serves both):
```json
{ "type":"detections", "frame":123, "width":640, "height":480,
  "detections":[ { "label":"cup", "conf":0.82, "bbox":[x1,y1,x2,y2] } ] }
```

---

## 4. Hosting topology

The client and server are hosted **independently**. Pick one option from each
column; the middle column is what makes them reach each other.

| Client (static) | Reachability (signaling `wss`/`ws` + WebRTC media) | Server (inference) |
|-----------------|----------------------------------------------------|--------------------|
| **Cloudflare Pages** (this repo's CI) | **LAN** — same Wi-Fi, `ws://`, media direct | **Mac mini M2 native** (MPS GPU) ← recommended |
| Any static host / `npm run dev` | **Tailscale** — flat WireGuard net, direct, no TURN | Docker (CPU) on a Linux box |
| Served from the headset | **Cloudflare Tunnel / Caddy** — `wss` signaling **+ TURN** for media | Hugging Face Spaces (free GPU) |
|  | **Router port-forward + DDNS + TURN** |  |

Key rules:
- A **hosted (HTTPS) client** must dial **`wss://`** (mixed-content rule). LAN dev
  over `http://localhost` can use `ws://`.
- A **reverse proxy carries signaling, not media.** Cross-NAT media needs **TURN**
  (or use **Tailscale**, which gives a direct path and needs neither proxy nor
  TURN). See [Configuration-and-Connectivity §5](Configuration-and-Connectivity.md#5-stun--turn--the-media-path-the-usual-no-video-cause).
- **Why not host inference on Cloudflare?** No free GPU; Workers can't run the
  torch/ultralytics stack. Cloudflare is used for the **client** (Pages) and
  optionally **signaling/TURN**, not inference.

### Recommended topologies

- **Home / LAN demo (lowest latency):** client on Cloudflare Pages, server native
  on the Mac mini M2, headset on the same Wi-Fi, `QVS_SIGNALING_URL=ws://<lan-ip>:3000`,
  no TURN. (If the hosted page blocks `ws://`, open with `?server=ws://<lan-ip>:3000`
  or put the server behind local TLS.)
- **Anywhere / personal:** add **Tailscale** to the Mac and the Quest;
  `QVS_SIGNALING_URL=ws://100.x.x.x:3000` (or `tailscale serve` for `wss`). Direct
  media, no TURN.
- **Public demo:** Cloudflare Tunnel for `wss` signaling + a TURN server; or the
  server on HF Spaces (GPU) + TURN.

---

## 5. Configuration surfaces (where knobs live)

| Concern | Where | Editable |
|---------|-------|----------|
| Which server the client dials | Pages KV `QVS_CONFIG`/`signaling_url` (live) or env var `QVS_SIGNALING_URL` (redeploy) via `/api/config`, or `?server=` | KV: live · env var: redeploy · `?server=`: per-open |
| Client render knobs (camera res, invertY, placement, dedup) | `quest-client/src/config.ts` `AppConfig` | Build time |
| Server host/port/detector/flips/ICE/TURN/YOLO levers | `QVS_*` env vars | Restart |
| Client deploy (projects, branch, short codes) | `.github/workflows/quest-client-deploy.yml` | Repo |

---

## 6. Performance & scaling notes

- **Latency levers (server):** `QVS_YOLO_IMGSZ` (biggest), `QVS_YOLO_HALF` (FP16 on
  MPS/CUDA), keep the server on-LAN. Detectors return detections only (no per-frame
  drawing) to keep the hot path lean.
- **Edge gate (client):** the brightness qualifier avoids streaming useless frames.
- **Adaptive resolution:** the client normalizes boxes against the *server-reported*
  frame size, so the server can ramp resolution up over time.
- **Concurrency:** one detector model is loaded and shared; each connection gets its
  own `VideoProcessor`. This targets a single headset; multiple concurrent headsets
  would want per-connection model instances or a batching queue (not implemented).
- **Placement depth (client, v1):** tags are placed at a fixed distance along the
  detection ray. True surface anchoring via IWSDK scene-understanding (`XRMesh` /
  environment depth) is the next enhancement; the renderer seam is already in place.
