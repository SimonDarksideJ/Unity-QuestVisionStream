# Modernization Handover Notes

> **⚠️ CORRECTION (2026-07-01, verified against git):** The "What's been done"
> section below describes code (server rewrite, `web/` WebXR client) that lived on
> a different branch (`claude/yolo-streaming-modernize-vaxefd`). **That work is not
> present on this branch or in any reachable git ref** — this tree is the original
> upstream code plus only `HANDOVER.md` + `mcp.json`. Every "fixed" bug below is
> still live. Read this document as *research/decisions*, not completed work.
> See `EVALUATION.md` for the verified current-state assessment.

Working context for the QuestVisionStream modernization effort, so a fresh
session can resume without re-investigating. Written 2026-07-01.

- **Branch:** `claude/yolo-streaming-modernize-vaxefd`
- **Goal:** (1) rebuild the streaming service to be self-hostable (chosen host:
  **native on a Mac Mini M2**, with HF Spaces as a free-cloud fallback); (2)
  performance pass; (3) migrate the Unity client/package to a **Meta IWSDK
  WebXR / TypeScript** app replicating the behaviour.

## TL;DR of decisions

- **Host:** native macOS (venv + MPS) for lowest latency + M2 unified memory;
  reverse-proxy (Cloudflare Tunnel / Caddy) for optional online access.
- **Cloud fallback:** Hugging Face Spaces (free GPU). Cloudflare has **no** free
  GPU — only usable for signaling/TURN, not inference.
- **WebXR migration is viable** (see camera findings below).

## KEY FINDING — Passthrough / RGB camera support (read this first)

There are **two different APIs**, and conflating them caused an initial wrong
conclusion. The distinction is the crux of the whole WebXR migration:

1. **WebXR *Raw* Camera Access** — the `camera-access` feature descriptor /
   `XRWebGLBinding.getCameraImage()`, giving the per-eye **passthrough texture**
   aligned to the XR view.
   - Status: **NOT shipped in Meta Horizon Browser** as of 2026 release notes
     (latest added WebGPU + WebXR depth projection, not camera-access). Spec
     exists; Chrome supports it; open Meta forum request with no ship date.
   - QuestVisionStream **does not need this.**

2. **IWSDK Camera Access** (Guide Ch. 13) — camera frames via the browser
   **MediaDevices API (`getUserMedia`)**, auto-managed on XR session enter/exit.
   - Status: **available now in IWSDK.** This is sufficient to source frames for
     the WebRTC track / on-device CV.
   - **This is the path** the WebXR client uses (`facingMode: 'environment'`).

> Bottom line: the full WebXR replica is unblocked. The only thing gated on IWSDK
> docs is the *renderer* (mapping a detection to a world anchor via WebXR
> hit-test + IWSDK ECS), not the camera capture.

Sources:
- IWSDK Guide Ch.13 Camera Access — https://iwsdk.dev/guides/13-camera-access.html
- WebXR Raw Camera Access spec — https://immersive-web.github.io/raw-camera-access/
- Meta MR-in-browser — https://developers.meta.com/horizon/documentation/web/webxr-mixed-reality/
- Meta web release notes — https://developers.meta.com/horizon/release-notes/web/
- Camera-access request thread — https://communityforums.atmeta.com/discussions/Questions_Discussions/request-webxr-raw-camera-access-camera-access-feature-in-quest-browser/1367463

## What's been done (on this branch)

### Server (`QuestVisionStreamServer/`)
- **Fixed detector dispatch bug:** `server.py` `--detector` choices didn't match
  `detectors/__init__.py` keys. `owl2`→`owlv2`; exposed `grounding_dino` (was
  documented but unreachable). Fixed `body` callback pushing an ndarray over the
  detections data channel.
- **Headless + env-driven:** rewrote `config.py` (all `QVS_*` env vars; display
  off by default); guarded `cv2.imshow` for `opencv-python-headless`.
- **Configurable ICE/STUN/TURN**; HTTP health endpoint on the WS server.
- **Deps split + pinned:** base `requirements.txt` + per-detector extras
  (`requirements-{yolo,florence2,zeroshot,body}.txt`).
- **Latency levers:** `QVS_YOLO_IMGSZ`, `QVS_YOLO_HALF`.
- **Deployment:** `run-local.sh` (native Mac, sets MPS unified-memory env),
  `Dockerfile`/`docker-compose.yml` (CPU fallback), `DEPLOY_LOCAL_MAC.md`,
  `DEPLOY_HF_SPACES.md`, `reverse-proxy/Caddyfile.example`, server `README.md`.

### WebXR/TS client (`web/`)
- `@questvisionstream/webxr-client` — typecheck-clean (tsc strict).
- `QuestVisionStreamClient`: `getUserMedia` + `RTCPeerConnection` speaking the
  **exact** protocol of `webrtc_server.py`:
  - signaling JSON: `offer`/`answer`/`candidate` over WebSocket;
  - client **creates** the `detections` data channel (server only listens);
  - **ICE-candidate prefix quirk:** aiortc sends/expects the SDP candidate line
    *without* the `candidate:` prefix — the bridge strips on send, re-adds on
    receive;
  - client **adds** the camera video track; server runs detection.
- `DetectionRenderer` seam + `attachRenderer()` port the Unity
  `DetectionSpawnerManager` bbox→viewport math + per-class dedup.
- Wire types identical to Unity `DetectionsPayload`/`Detection`.

## Architecture / protocol reference

```
Camera ──(WebRTC video track)──▶ webrtc_server.py ──▶ VideoProcessor ──▶ detector
       ◀──(data channel "detections": {type,frame,width,height,detections[]})──
```
Detection = `{ label: string, conf: float, bbox: [x1,y1,x2,y2] (stream px) }`.
The **same server serves both the Unity and WebXR clients unchanged.**

## Hosting gotchas (Apple Silicon)

- Docker Desktop on macOS: **no MPS GPU** (CPU only) and **no `--network host`**
  → WebRTC media usually needs TURN. So **run native** on the Mac Mini.
- **On-LAN:** `QVS_ENABLE_TURN=false` → media flows headset↔Mac directly.
- **Remote/online:** `QVS_ENABLE_TURN=true` (relay needed to cross NATs).
- Native MPS env (set by `run-local.sh`): `PYTORCH_ENABLE_MPS_FALLBACK=1`,
  `PYTORCH_MPS_HIGH_WATERMARK_RATIO=0.0` (use full unified memory).

## Outstanding work

1. **IWSDK renderer** — implement `DetectionRenderer.place()`: WebXR hit-test
   through the normalized viewport point → spawn a tag entity in the IWSDK ECS
   (equivalent of Unity EnvironmentRaycast + DetectionTag prefab). Needs the
   IWSDK API (unblocked once docs allowlisted / MCPs connect).
2. Scaffold a runnable app via `npm create iwsdk`, drop in `web/`, wire renderer.
3. Deeper perf pass: ONNX/CoreML export for the M2, adaptive resolution.

## Environment setup required (host actions)

- **Network access → Custom** (edit environment at claude.ai/code), add:
  `iwsdk.dev`, `*.iwsdk.dev`, `developers.meta.com`, `*.meta.com`
  (optionally `huggingface.co`, `*.hf.co` for MCP corpus warmup). Keep the
  default package-manager list checked.
- **MCPs:** see `.mcp.json` (iwsdk-rag, iwsdk-runtime, iwsdk-reference, hzdb).
  Loaded at session start — verify exact npx args against the IWSDK
  getting-started page (https://iwsdk.dev/ai/getting-started.html) once
  allowlisted. MCP config is read from whatever branch the session starts on.
