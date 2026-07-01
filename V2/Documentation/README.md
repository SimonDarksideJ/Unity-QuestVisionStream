# QuestVisionStream V2 — Documentation

Guides and deployment references for the V2 stack.

## Contents

- **[Install-Mac-M2.md](Install-Mac-M2.md)** — install and run the streaming
  service natively on a Mac mini M2 (macOS 26.5) using the full unified memory +
  GPU (Metal/MPS), and expose it externally (Tailscale / Cloudflare Tunnel / Caddy
  / router port-forward, plus TURN for WebRTC media across NAT).

- **[Deployment-Cloudflare-Pages.md](Deployment-Cloudflare-Pages.md)** — deploy the
  WebXR client to Cloudflare Pages (isolated production + staging projects) via
  GitHub Actions, with per-deploy summary + short code + QR. Preparation checklist
  for the `CLOUDFLARE_*` secrets, and where to set `QVS_SIGNALING_URL`.

- **[Architecture-and-Hosting.md](Architecture-and-Hosting.md)** — the big picture:
  components (client / library / service-framework / server), end-to-end data flow,
  and the client + server hosting topology (LAN / Tailscale / tunnel / TURN).

- **[Configuration-and-Connectivity.md](Configuration-and-Connectivity.md)** — how
  the client is configured (which server it dials), the WebRTC connection sequence,
  ports, STUN/TURN, and a connectivity troubleshooting matrix.

- **examples/**
  - [`com.questvisionstream.plist`](examples/com.questvisionstream.plist) —
    launchd LaunchAgent to run the server as an auto-starting background service
    with the MPS environment.
  - [`cloudflared-config.yml`](examples/cloudflared-config.yml) — Cloudflare Tunnel
    config exposing signaling over WSS with no port-forwarding.

## Related docs elsewhere in the repo

- `../QuestVisionStreamServer/README.md` — server overview and every `QVS_*` var.
- `../QuestVisionStreamServer/DEPLOY_LOCAL_MAC.md` — condensed native-macOS notes.
- `../QuestVisionStreamServer/DEPLOY_HF_SPACES.md` — Hugging Face Spaces (cloud GPU).
- `../QuestVisionStreamServer/deploy/Caddyfile.example` — Caddy reverse proxy.
- `../README.md` — V2 architecture overview.
