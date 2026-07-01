# QuestVisionStream V2

A fresh rebuild of QuestVisionStream. The V1 tree (Unity project + Python server
at the repo root) is left untouched and serves as **reference only**; all new work
lives here under `V2/`.

## What's here

```
V2/
├── service-framework/        # @realitycollective/service-framework-ts
│                             #   Faithful TS port of the RealityCollective Service
│                             #   Framework architecture (ServiceManager DI + lifecycle).
├── com.questvisionstream/    # @questvisionstream/client  (the reusable library)
│                             #   Streaming client as Service Framework services;
│                             #   file-linked into the Quest client.
├── quest-client/             # Meta IWSDK WebXR app (the new Quest client)
│                             #   Thin host that wires the library into IWSDK's ECS.
├── QuestVisionStreamServer/  # Modernized Python inference server.
└── Documentation/            # Install + deployment guides (see below).
```

## Documentation

- **[Documentation/Install-Mac-M2.md](Documentation/Install-Mac-M2.md)** — run the
  server natively on a Mac mini M2 (macOS 26.5) with full unified memory + GPU
  (Metal/MPS), and expose it externally (Tailscale / Cloudflare Tunnel / Caddy /
  router, plus TURN). Ready-to-use launchd + Cloudflare examples in
  `Documentation/examples/`.

Two "app" folders as requested — the **Quest client** and the updated
**QuestVisionStreamServer** — plus the reusable **`com.questvisionstream`** library
(file-linked from the client, not copied) and the **Service Framework** it builds on.

## Architecture at a glance

```
┌─────────────────────── quest-client (IWSDK / Three.js ECS) ───────────────────────┐
│  ServicePumpSystem ──▶ ServiceManager.update(dt)                                   │
│  CameraStreamSystem ──▶ CameraSource → MediaStream ─┐        DetectionRenderSystem  │
│                          + edge quality gate        │        (IDetectionRenderer)   │
└─────────────────────────────────────────────────────┼────────────────▲─────────────┘
                                                       │  resolve by    │ detections
                        ServiceManager (DI, by token)  │  interface     │
┌──────────────────── com.questvisionstream (services) ▼────────────────┴─────────────┐
│  ISignalingService ──▶ IImageQualifierService ──▶ IWebRTCService ──▶ IDetectionService│
│        (WS)               (brightness module)     (peer conn + DC)     (parse)        │
└───────────────────────────────────┬──────────────────────────────────────────────────┘
                                     │ WebRTC (video track ▲ / detections DC ▼)
                          ┌──────────▼───────────┐
                          │ QuestVisionStreamServer │  aiortc + detector (YOLO/…)
                          └────────────────────────┘
```

- **DI throughout:** every streaming concern is a `ServiceManager`-orchestrated
  service, resolved by **interface token** (`getService(IWebRTCService)`). IWSDK
  systems consume services by token; they never import a concrete service class.
- **Reusability:** all streaming logic is in `com.questvisionstream`, file-linked
  (`file:../com.questvisionstream`) into the client. Swap the host (IWSDK, a DOM
  overlay, tests) and reuse the library unchanged.
- **Protocol fidelity:** the library's wire types match the server and the Unity
  client byte-for-byte, so one server serves both clients.

## Build & run

**Requirements:** Node 18+, Python 3.11+.

```bash
# TypeScript side (build order matters: framework → library → client)
cd service-framework && npm install && npm run build && cd ..
cd quest-client && npm install && npm run dev      # Vite; open ?server=ws://<server>:3000
# (the client dev/build aliases the libraries to source — no prebuild needed for Vite)

# Server
cd QuestVisionStreamServer && ./run-local.sh        # native macOS/MPS, or: docker compose up --build
```

From the V2 root, `npm run build` / `npm run typecheck` run all three TS packages
in dependency order.

## Verification status

- `service-framework`, `com.questvisionstream`, `quest-client`: **`tsc` clean**;
  the client also produces a full **`vite build`** against real `@iwsdk/core@0.4.2`
  \+ `three`.
- `QuestVisionStreamServer`: syntax-checked; config + detector registry + dispatch
  guard unit-tested without heavy deps.
- On-headset runtime and live inference require the actual hardware/models and are
  the next validation step. See each folder's README for details and known limits
  (notably: detection depth placement is fixed-distance in v1 — surface anchoring
  via IWSDK scene-understanding is the next enhancement).

See `../EVALUATION.md` and `../UNITY_REFERENCE_FEATURES.md` for the analysis that
drove this rebuild.
