# V2/tools

Automation for running the **V2** QuestVisionStream **streaming server** on a Mac
and exposing it to a Meta Quest over Tailscale.

> This runs the *server* only. The WebXR **client** is hosted separately
> (Cloudflare Pages) and discovers the server at runtime via its
> `/api/config` → `QVS_CONFIG`/`signaling_url` KV value. These scripts do **not**
> build or serve the client.

## `setup-tailscale-mac.sh`

```bash
cd V2/tools
./setup-tailscale-mac.sh
```

### What it does

1. **External-storage hygiene** (repo lives on `/Volumes/...`): clears macOS
   `com.apple.quarantine`, verifies the volume is writable and not `noexec`, and
   marks the shell scripts executable.
2. **Prereqs** — `python3` (warns if not arm64 → no MPS GPU) and the Tailscale CLI
   (Homebrew **or** the Mac App Store app). No Node/npm needed to run the server.
3. **Tailscale** — starts the `tailscaled` daemon if it isn't running
   (`sudo brew services start tailscale` for Homebrew installs), logs in with
   `--operator=$USER` if needed, and reads this machine's MagicDNS name.
4. **Streaming server** — launches the Python signaling/WebRTC server via
   [`run-local.sh`](../QuestVisionStreamServer/run-local.sh) (venv + deps + MPS
   env, GPU-accelerated) and waits for its health endpoint.
5. **HTTPS/WSS exposure** — `tailscale serve` fronts the local `ws://` server with
   an auto-provisioned Let's Encrypt cert:

   ```text
   wss://<machine>.<tailnet>.ts.net/
   ```

6. **Cloudflare KV** — if wrangler is authenticated, pushes that URL into the
   `signaling_url` key of the KV namespace bound as `QVS_CONFIG`, so the hosted
   client picks it up **live** (no redeploy). See *Cloudflare wiring* below.
7. **Prints** the signaling URL and the exact `wrangler` command to set it
   manually if the auto-push is skipped.

WebRTC media then flows **directly over WireGuard — no TURN** — as long as the
Quest is signed into the **same tailnet**.

### Options

| Flag | Effect |
|------|--------|
| `--detach` | Set up, print the URL, and leave the server running (stop later with `stop-tailscale-mac.sh`). |
| `--reset` | Clear this machine's existing Tailscale `serve` config first. |
| `--no-push-kv` | Don't touch Cloudflare KV; just run + print the URL. |

Env passthrough: `QVS_DETECTOR` (default `yolo`), `QVS_YOLO_IMGSZ`,
`QVS_YOLO_HALF`, `QVS_YOLO_CONF`, `QVS_AUTH_TOKEN` (appended to the URL when set),
`SIGNAL_PORT` (3000), `QVS_HEALTH_PORT` (8080),
`QVS_CF_KV_NAMESPACE_ID` (override the baked KV namespace id).

## `stop-tailscale-mac.sh`

Tears down a `--detach` run: stops the streaming server and removes the Tailscale
serve mount.

## Cloudflare wiring (already set up)

The KV live-config path is provisioned in the `sjackson@ethar.com` account:

- **KV namespace** `qvs-config` (id `dadee36b3126445fb6d0b1ca0620402e`), key
  `signaling_url`.
- **Binding** `QVS_CONFIG` → that namespace on the `questvisionstream` **and**
  `questvisionstream-test` Pages projects (production + preview).

> ⚠️ **One-time redeploy:** a Pages project only attaches a binding on its **next
> deploy**. Redeploy each project once so `QVS_CONFIG` becomes active in the live
> deployment. After that, `signaling_url` **value** changes (what this script
> pushes) are picked up live — just reload the client.

To (re)authenticate wrangler: `npx wrangler login`. To read the current value:
`npx wrangler kv key get --namespace-id=dadee36b3126445fb6d0b1ca0620402e signaling_url --remote`.

## Notes

- HTTPS certs must be enabled for your tailnet (Tailscale admin console → **DNS →
  HTTPS Certificates**); the script points you there if `serve` can't get a cert.
- First server launch downloads the YOLO weights (`yolo11n.pt`), so step 4 can
  take a minute — watch `.run/server.log`.
- See [`../Documentation/Install-Mac-M2.md`](../Documentation/Install-Mac-M2.md) §5.1
  and [`../Documentation/Deployment-Cloudflare-Pages.md`](../Documentation/Deployment-Cloudflare-Pages.md) §4.
