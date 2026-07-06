# QuestVisionStream V2 — Documentation

The single hub for all V2 documentation. Everything below lives here under
`Documentation/`; component packages keep only a short README that points back here.

## Start here

- **[Architecture-and-Hosting.md](Architecture-and-Hosting.md)** — the big picture:
  components, end-to-end data flow, and where each half runs. Read this first.
- **[../README.md](../README.md)** — V2 repo layout and build/run quickstart.

## Guides

| Guide | What it covers |
|-------|----------------|
| [Unity-Client.md](Unity-Client.md) | **The V2 Quest client** (Unity 6, Quest 3/3S): Service Framework architecture, Meta passthrough camera access, transport decision, render modes, AprilTags, build steps. |
| [Configuration-and-Connectivity.md](Configuration-and-Connectivity.md) | How the client is configured (which server it dials), the WebRTC connection sequence, ports, STUN/TURN, and a connectivity troubleshooting matrix. |
| [Install-Mac-M2.md](Install-Mac-M2.md) | Run the server natively on a Mac mini M2 (macOS 26.5) using the GPU + full unified memory (MPS), and expose it externally (Tailscale / Cloudflare Tunnel / Caddy / router + TURN). |
| [Quest-Headset-Setup.md](Quest-Headset-Setup.md) | Set up the **Meta Quest**: sideload the Tailscale APK (not in the Horizon Store), join the same tailnet, open the hosted client, and troubleshoot the on-headset status/connection. |
| [Deploy-HuggingFace-Spaces.md](Deploy-HuggingFace-Spaces.md) | Run the server on Hugging Face Spaces (free cloud GPU). |
| [Deployment-Cloudflare-Pages.md](Deployment-Cloudflare-Pages.md) | Deploy the WebXR client to Cloudflare Pages (isolated prod + staging) via GitHub Actions; create the `CLOUDFLARE_*` secrets; configure the server URL (KV binding / env var / `?server=`). |

## Reviews & improvements

- **[Architecture-Review-2026-07.md](Architecture-Review-2026-07.md)** — verified
  technical review of the whole V2 tree (what works, bugs with file:line refs,
  security posture, prioritized fixes).
- **[improvements/](improvements/README.md)** — the improvement log + the
  red→green→measure method guide. Each entry records why a change was made and
  the before/after test stats. First entry:
  [2026-07 server hardening](improvements/2026-07-Server-Hardening.md).

## Reference

- **[IWSDK-API-Reference.md](IWSDK-API-Reference.md)** — source-verified `@iwsdk/core`
  API snapshot the client was built against.
- **examples/** — ready-to-use config:
  - [`com.questvisionstream.plist`](examples/com.questvisionstream.plist) — launchd
    LaunchAgent to run the server as a background service (MPS env baked in).
  - [`cloudflared-config.yml`](examples/cloudflared-config.yml) — Cloudflare Tunnel
    for `wss` signaling with no port-forwarding.
  - [`Caddyfile.example`](examples/Caddyfile.example) — Caddy reverse proxy.

## Component docs (package READMEs)

- [`../README.md`](../README.md) — V2 overview & architecture.
- [`../quest-client/README.md`](../quest-client/README.md) — the IWSDK WebXR client app.
- [`../com.questvisionstream/README.md`](../com.questvisionstream/README.md) — the
  reusable streaming library.
- [`../QuestVisionStreamServer/README.md`](../QuestVisionStreamServer/README.md) —
  the Python inference server (all `QVS_*` env vars).

## Background / analysis (repo root)

- [`../../EVALUATION.md`](../../EVALUATION.md) — evaluation that drove the V2 rebuild.
- [`../../UNITY_REFERENCE_FEATURES.md`](../../UNITY_REFERENCE_FEATURES.md) — catalog of
  Unity client-led features recreated in the WebXR client.
- [`../../HANDOVER.md`](../../HANDOVER.md) — original modernization research notes.
