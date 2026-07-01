# Configuration & Connectivity Guide

How the WebXR client is configured and how it connects to the streaming server —
plus a troubleshooting matrix for "it loaded but won't connect".

Related: [Architecture-and-Hosting.md](Architecture-and-Hosting.md) (the big
picture), [Deployment-Cloudflare-Pages.md](Deployment-Cloudflare-Pages.md)
(deploying the client), [Install-Mac-M2.md](Install-Mac-M2.md) (running the
server + remote access).

---

## 1. The two moving parts

```
   ┌─────────────────────────┐            ┌────────────────────────────┐
   │  WebXR client (static)  │  WebRTC    │  QuestVisionStreamServer   │
   │  Cloudflare Pages /      │◀─────────▶ │  aiortc + detector (YOLO…) │
   │  any static host / Quest │  signaling │  your Mac / Docker / cloud │
   └─────────────────────────┘   + media   └────────────────────────────┘
```

The client is a **static bundle** — it has no server baked in. It must be *told*
which signaling server to dial. The server needs to be **reachable** from the
headset's network (LAN, Tailscale, tunnel, or public).

---

## 2. How the client is configured (which server it dials)

Resolved at startup in `V2/quest-client/src/config.ts`, **first match wins**:

| Priority | Source | Use it for |
|---------:|--------|------------|
| 1 | **`?server=` URL param** — `…/?server=wss://host:3000` | One-off tests / overriding everything |
| 2 | **`/api/config`** — Cloudflare Pages Function returning the project env var `QVS_SIGNALING_URL` | **The normal way.** Editable online, per project, no rebuild |
| 3 | **`VITE_SIGNALING_URL`** — baked at build time | A compile-time default (optional) |
| 4 | **`ws://localhost:3000`** | Local dev |

**Recommended setup:** deploy once, then set `QVS_SIGNALING_URL` on each
Cloudflare Pages project (Settings → **Variables and Secrets** → Plaintext). See
[Deployment-Cloudflare-Pages.md §4](Deployment-Cloudflare-Pages.md#4-point-the-deployed-app-at-your-streaming-server-online-editable)
for the exact clicks. Then the plain apex URL / QR "just works".

> **`wss://` vs `ws://`:** the deployed page is HTTPS, so browsers **block plain
> `ws://`** as mixed content. Your server must be reachable over **`wss://`** (TLS)
> for a hosted client. On a `http://localhost` dev page, `ws://` is fine.

The other configurable knobs live in `AppConfig` (same file): camera resolution,
`invertY`, tag placement distance, dedup policy.

---

## 3. The connection sequence

Once the client has a signaling URL, connecting is a WebRTC handshake:

```
Client                                              Server (aiortc)
  │  1. WebSocket connect  ws(s)://host:3000  ─────────▶│
  │  2. getUserMedia(camera) → local video track        │
  │  3. create RTCPeerConnection                         │
  │     + create "detections" DataChannel (client-made)  │
  │     + add camera video track                         │
  │  4. createOffer → setLocalDescription                │
  │  5. {type:"offer", sdp} ───────────────────────────▶│ setRemoteDescription
  │                                                      │ createAnswer
  │  6. ◀─────────────────────────── {type:"answer", sdp}│ setLocalDescription
  │  7. ICE candidates  ◀──── {type:"candidate", …} ────▶│   (trickle both ways)
  │  8. ── DTLS/SRTP media path established ──            │
  │  9. camera frames  ═══════════════════════════════▶ │ VideoProcessor → detector
  │ 10. ◀═══ "detections" DataChannel {frame,w,h,boxes} ═│ per-frame results
  └── DetectionRenderSystem places world-anchored tags ──┘
```

Notes that matter for interop:
- The **client** creates the `detections` data channel; the server only listens.
- The client is the **offerer** and **adds the camera track**.
- **ICE `candidate:` prefix quirk:** aiortc emits/expects the SDP candidate line
  *without* the `candidate:` prefix; the client strips it on send and re-adds it on
  receive (`WebRTCService`). If you swap in a different server, keep this in mind.

---

## 4. Ports & what must be reachable

| Port | Proto | Purpose | Must the headset reach it? |
|------|-------|---------|----------------------------|
| **3000** | WebSocket (TCP) | Signaling (`ws`/`wss`) | **Yes** — this is the `QVS_SIGNALING_URL` |
| **8080** | HTTP (TCP) | Health (`GET /`) | No — ops probe only; keep internal |
| ephemeral | UDP/TCP | **WebRTC media** (the actual video) | **Yes**, but the path is negotiated via ICE (see below) |

The signaling port is a plain WebSocket you point the client at. The **media**
does *not* go over that port — it's a separate ICE-negotiated path.

---

## 5. STUN / TURN — the media path (the usual "no video" cause)

WebRTC uses **ICE** to find a path for media:

- **STUN** just discovers your public address. On a **LAN** (or Tailscale), the
  headset and server reach each other directly → **no TURN needed**
  (`QVS_ENABLE_TURN=false`, the default).
- **TURN** is a relay used when both peers are behind NAT (typical for a headset on
  Wi-Fi/cellular + a server behind a home router across the internet). Without a
  reachable direct path, **media silently fails even though signaling succeeded** —
  the classic "connects but no video / no detections".

Configure TURN on the **server** (see
[Install-Mac-M2.md §5.5](Install-Mac-M2.md#55-turn-required-for-media-on-52--53--54)):

```bash
QVS_ENABLE_TURN=true
QVS_TURN_URLS=turn:your.turn.host:3478
QVS_TURN_USERNAME=user
QVS_TURN_CREDENTIAL=pass
```

**Tailscale sidesteps all of this** — it gives a direct path, so no TURN and no
reverse proxy. Recommended for personal use.

---

## 6. Connectivity troubleshooting matrix

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| App loads, **never connects**, console shows `ws://localhost:3000` | No server configured | Set `QVS_SIGNALING_URL` (Deployment §4) or open with `?server=wss://host:3000` |
| Console: **mixed content / insecure WebSocket blocked** | HTTPS page dialing `ws://` | Use `wss://` (TLS on the server via tunnel/reverse proxy), or serve the client over http on localhost |
| Signaling connects, **no video / no detection tags** | Media can't traverse NAT | Enable **TURN** on the server, or use **Tailscale**. Confirm with `curl http://server:8080/` |
| Works on **LAN**, fails **remote** | No public path / no TURN | TURN + `wss://` signaling (tunnel), or Tailscale |
| **Camera permission** prompt denied / black | getUserMedia blocked | Grant camera permission in the Horizon Browser; must be a secure context (`https`/`wss`) |
| `/api/config` returns `{"server":""}` | Pages env var not set | Add `QVS_SIGNALING_URL` on **that** project + environment; reload |
| Changed the Cloudflare var but app still uses the old one | Cached page | Hard-reload; the Function is `no-store`, but the SPA may be cached |
| Detections lag behind head movement | Network round-trip latency | Expected to a degree; lower `QVS_YOLO_IMGSZ`, keep the server on-LAN |

### Quick verification

```bash
# 1. Server up?
curl http://<server-host>:8080/           # {"status":"ok","detector":"yolo",...}

# 2. What server will the deployed client use?
curl https://questvisionstream.pages.dev/api/config   # {"server":"wss://…"}

# 3. Force a server for one session (bypasses all config):
#    open  https://questvisionstream.pages.dev/?server=wss://<host>:3000  on the Quest
```

---

## 7. Local development

```bash
# Server (native, LAN):
cd V2/QuestVisionStreamServer && ./run-local.sh       # ws://<lan-ip>:3000

# Client dev server:
cd V2/quest-client && npm run dev                      # http://localhost:5173
# open http://localhost:5173/?server=ws://<lan-ip>:3000   (ws is fine on http)
```

In dev, `/api/config` 404s (no Function) and the client falls through to
`?server=` or the localhost default — exactly as designed.
