# QuestVisionStream — Modernization Evaluation

Written 2026-07-01. Scope: evaluate the two components (streaming service +
Unity client/package) and assess the modernization research in `HANDOVER.md`.
**This is an evaluation only — no code was changed.**

---

## 0. Branch reality (read first)

`HANDOVER.md` is written as though a server rewrite and a `web/` WebXR client
already exist ("What's been done (on this branch)"). **They do not exist on this
branch, nor in any reachable git ref.**

- This branch (`claude/yolo-streaming-unity-modernize-2stzq2`) is byte-identical
  to `origin/IWSDK`: the original upstream code plus exactly two added files
  (`HANDOVER.md`, `mcp.json`), committed together as "Add warm up files".
- The handover references a different branch (`claude/yolo-streaming-modernize-vaxefd`).
  That branch is not present locally or on `origin`; `git log --all` finds none
  of its artifacts (`web/`, `run-local.sh`, `DEPLOY_*.md`, `webxr-client`).
- **Consequence:** every bug the handover claims to have fixed is still live in
  this tree. Treat the handover as *research to act on*, not *work to build on*.

---

## 1. Streaming service (`QuestVisionStreamServer/`)

### Architecture (as-is)

```
Quest ──(WebRTC video track)──▶ webrtc_server.py ──▶ VideoProcessor ──▶ detector
      ◀──(data channel "detections": {type,frame,width,height,detections[]})──
```

`Detection = { label: string, conf: float, bbox: [x1,y1,x2,y2] (stream px) }`.
Detectors: `yolo`, `florence2`, `owlv2`, `grounding_dino`, `body` (lazy-loaded).

### Confirmed defects (current code)

| # | Severity | Location | Issue |
|---|----------|----------|-------|
| 1 | High | `server.py:12` | `--detector` choices `['yolo','florence2','owl2','body']` don't match registry keys. `owl2` ≠ `owlv2` → fails; `grounding_dino` is unreachable from the CLI. |
| 2 | High | `video_processor.py:56,112` / `config.py:1` | Not headless-safe: `ENABLE_DISPLAY=True` default + unconditional `cv2.imshow`/`cv2.destroyAllWindows`. Breaks on any server host / `opencv-python-headless`. |
| 3 | High | `webrtc_server.py:88` | ICE candidates serialized with aiortc `candidate_to_sdp`, which omits the `candidate:` prefix that a browser `RTCIceCandidate` expects. Direct interop blocker for a WebXR/JS client (must strip on send / re-add on receive). |
| 4 | Med | `config.py`, `webrtc_server.py:31-38` | No configuration surface. Host/port/flip/STUN/TURN are hardcoded constants; openrelay TURN creds baked in. Not cleanly hostable or environment-portable. |
| 5 | Med | `requirements.txt` | Unpinned + monolithic: `torch`, `transformers`, `florence2`, `mediapipe` all installed even for a YOLO-only run. Non-reproducible, heavy image. |
| 6 | Low | `video_processor.py:80-84` | FPS math: `last_fps_time` starts at 0, so the first logged FPS is meaningless; interval accounting is fragile. |
| 7 | Low | `yolo_detector.py:36-38` | Draws `cv2.rectangle`/`putText` on every frame even when headless — pure waste on the hot path. `IGNORE_CLASSES` hardcoded. |
| 8 | Low | `yolo_detector.py:15` | Model loaded at import time (module side effect), so importing the module blocks on disk/GPU load; `MODEL_PATH` is a bare relative path. |

### Hostability assessment

The handover's hosting decision is **sound and I concur**:

- **Native macOS + MPS** for lowest latency and M2 unified memory. Docker Desktop
  on macOS gives no MPS (CPU-only) and no `--network host`, which pushes WebRTC
  media onto a TURN relay — worse latency. Run native on the Mac Mini.
- **Cloud fallback: Hugging Face Spaces (free GPU).** Reasonable.
- **Cloudflare has no free GPU** → usable only for signaling/TURN, not inference.
  Correct to reject it as the inference host. (Cloudflare Workers AI exists but
  won't run this torch/ultralytics stack, and Containers/GPU aren't free.)

### Recommended server work (when green-lit)

1. Fix the dispatch bug; make the CLI choices the single source of truth from the
   registry keys.
2. Make headless the default; guard all `cv2` display behind a config flag.
3. Env-driven config (`QVS_*`): host/port, display, flips, detector, ICE/STUN/TURN,
   YOLO `imgsz`/`half`, confidence, ignore-classes.
4. Split + pin deps: base `requirements.txt` + per-detector extras.
5. Add an HTTP health endpoint alongside the WS server.
6. Deployment artifacts: native run script (sets MPS env), `Dockerfile`/compose
   (CPU fallback), reverse-proxy example, per-target deploy docs.
7. Move the ICE-candidate prefix handling to a documented, tested seam so the JS
   client interops without surprises.

### Performance levers (ranked)

1. **Model input size** (`imgsz`) and **FP16** (`half=True` on MPS/CUDA) — biggest,
   cheapest wins.
2. **Drop per-frame drawing** on the headless path (defect #7).
3. **ONNX / CoreML export** of `yolo11n` for the M2 — meaningful throughput gain
   over the eager torch path on Apple Silicon.
4. **Adaptive resolution**: start low, ramp up (the Unity client already expects a
   server-reported `width/height` per frame — see `DetectionSpawnerManager.OnDetections`).
5. **Frame-drop / latest-frame-wins** in `process_video_stream` so inference never
   queues behind stale frames under load.

---

## 2. Unity client / package (`com.questvisionstream/`)

### What's actually here

- **WebRTC is a compiled Android `.aar`** (`QuestVisionStreamPlugin-release.aar`
  \+ libjingle `libjingle_peerconnection_so.so`). The Unity C# layer
  (`QuestVisionStreamBridge.cs`) is a thin `AndroidJavaObject` bridge over it.
  **The native peer-connection logic is a black box.** A WebXR port must re-derive
  the wire protocol from the *server*, not from this plugin.
- `PCAVideoStreamer.cs` — captures passthrough via Meta `WebCamTextureManager`,
  runs a GPU RGB→YUV420 compute shader, throttles sends (`sendEveryNFrame`).
  Depends on Meta `PassthroughCameraSamples`.
- `DetectionSpawnerManager.cs` — the behaviour worth replicating: bbox center →
  normalized (÷ stream w/h) → camera intrinsics resolution → `ScreenPointToRayInWorld`
  → `EnvironmentRayCast` → world-anchored `DetectionTag`, with per-class dedup and
  a `minDistanceMeters` gate for multi-instance.
- `QuestVisionStreamEvents.cs` — UnityEvent fan-out for connection/video/detections.

### WebXR migration feasibility

The handover's central distinction is **correct and is the crux of the whole
migration**:

1. **WebXR *Raw* Camera Access** (`camera-access` descriptor /
   `XRWebGLBinding.getCameraImage()`) — the per-eye passthrough texture. **Not
   shipped in Meta Horizon Browser** as of 2026 release notes. QuestVisionStream
   does **not** need it.
2. **IWSDK Camera Access** — frames via browser `getUserMedia`
   (`facingMode:'environment'`), auto-managed on XR session enter/exit.
   **Available now.** Sufficient to source frames for the WebRTC track. **This is
   the path.**

So **capture is unblocked**. The only piece gated on live IWSDK API docs is the
**renderer** — mapping a detection's normalized viewport point to a world anchor
via WebXR hit-test + the IWSDK ECS (the equivalent of Unity's
`EnvironmentRaycast` + `DetectionTag` prefab).

**Correction to the handover:** it describes the WebXR client as already built and
"typecheck-clean / protocol-exact." That code does not exist on any reachable ref.
The WebXR/TS client is a **from-scratch rebuild**, not an iteration.

### Migration plan (when green-lit)

1. `npm create iwsdk` scaffold; TypeScript, strict.
2. `QuestVisionStreamClient`: `getUserMedia` + `RTCPeerConnection` speaking the
   **exact** protocol of `webrtc_server.py`:
   - signaling JSON: `offer` / `answer` / `candidate` over WebSocket;
   - client **creates** the `detections` data channel (server only listens);
   - **handle the `candidate:` prefix quirk** (defect #3) on both send and receive;
   - client **adds** the camera video track.
3. Port the `DetectionSpawnerManager` bbox→viewport math + per-class dedup into a
   framework-agnostic `DetectionRenderer` seam.
4. Implement `DetectionRenderer.place()` against the live IWSDK API (hit-test →
   spawn tag entity in the ECS).
5. Keep wire types identical to Unity `DetectionsPayload`/`Detection` so **one
   server serves both clients unchanged**.

---

## 3. Assessment of `HANDOVER.md`

| Claim | Verdict |
|-------|---------|
| Host native macOS/MPS; HF Spaces fallback; Cloudflare no free GPU | ✅ Sound — concur |
| Raw Camera Access not in Meta browser; IWSDK `getUserMedia` is the path | ✅ Correct and central |
| WebXR migration is viable; only renderer gated on IWSDK docs | ✅ Agree |
| aiortc ICE `candidate:`-prefix interop quirk | ✅ Real; most important client-side gotcha |
| Docker-on-macOS: no MPS, no host networking → needs TURN | ✅ Correct |
| "What's been done (on this branch)" — server fixes + `web/` client | ❌ **Not present on this branch or any ref.** Must be redone. |
| WebXR client "typecheck-clean / protocol-exact" | ❌ No such code exists; from-scratch rebuild |

---

## 4. Recommended sequencing (for later implementation)

1. **Server rebuild** — highest ROI, self-contained, fully testable without a
   headset. Fixes defects #1–#8, adds hostability + deploy artifacts.
2. **WebXR/TS client** — scaffold + WebRTC bridge (protocol-exact, prefix fix) +
   ported bbox math.
3. **IWSDK renderer + perf pass** — ONNX/CoreML export, adaptive resolution;
   needs the IWSDK MCPs/docs live (they are reachable in this environment).

No code has been changed as part of this evaluation.
