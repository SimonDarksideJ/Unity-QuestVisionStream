# QuestVisionStreamServer on a Mac mini M2 (macOS 26.5)

A complete guide to running the streaming service **natively** on Apple Silicon —
using the full unified memory and the GPU (Metal / MPS) — and to exposing it
**externally** for a Quest headset that isn't on the same Wi‑Fi.

> Why native (not Docker) on macOS: Docker Desktop on macOS runs Linux in a VM
> with **no MPS GPU passthrough** (CPU‑only inference) and **no host networking**
> (WebRTC media then needs a relay). Running natively gets you the GPU, the
> unified memory, and — on your LAN — direct headset↔Mac media. Use Docker only
> as a CPU fallback on a Linux box.

---

## 0. TL;DR

```bash
# 1. Prereqs (Homebrew + Python)
brew install python@3.12 git

# 2. Get the server
git clone <your-repo-url> && cd <repo>/V2/QuestVisionStreamServer

# 3. Run it (creates .venv, installs deps, sets MPS env, starts)
./run-local.sh                      # detector defaults to yolo

# 4. Verify
curl http://localhost:8080/          # {"status":"ok","detector":"yolo",...}
```

Then pick a remote‑access path in [§5](#5-external-access). For "just works from
anywhere," **Tailscale** ([§5.1](#51-tailscale-recommended--easiest-and-most-reliable))
is the least‑effort, most reliable option.

---

## 1. Prerequisites

| Requirement | Notes |
|-------------|-------|
| macOS 26.5 on Apple Silicon (M2) | Unified memory + Metal GPU used via PyTorch MPS |
| **Homebrew** | https://brew.sh |
| **Python 3.11 or 3.12** | `brew install python@3.12`. Avoid the system Python. |
| **git** | `brew install git` |
| Xcode Command Line Tools | `xcode-select --install` (compilers for some wheels) |

Check your chip and memory:

```bash
sysctl -n machdep.cpu.brand_string      # Apple M2
sysctl hw.memsize                        # total unified memory in bytes
```

All of that memory is shared by CPU **and** GPU — that's the advantage we exploit
below.

---

## 2. Install the server

`run-local.sh` does everything (venv + the right dependency set for your chosen
detector + the Apple‑Silicon environment), but here it is explicitly so you know
what's happening:

```bash
cd V2/QuestVisionStreamServer

python3.12 -m venv .venv
source .venv/bin/activate
pip install --upgrade pip

# Base runtime (aiortc, websockets, numpy, headless OpenCV)
pip install -r requirements.txt

# Detector extras — pick ONE to match QVS_DETECTOR:
pip install -r requirements-yolo.txt          # yolo (default, best on MPS)
# pip install -r requirements-zeroshot.txt    # owlv2 / grounding_dino
# pip install -r requirements-florence2.txt   # florence2 (prefers CUDA; slow on MPS)
# pip install -r requirements-body.txt        # body (MediaPipe)
```

PyTorch's arm64 wheels ship with the **MPS** backend built in — no CUDA, no extra
steps. The YOLO detector auto‑downloads its weights (`yolo11n.pt`) on first run.

---

## 3. Use the full unified memory + GPU (MPS)

Two environment variables unlock Apple Silicon. `run-local.sh` already exports
them; set them yourself if you run `python -m questvisionstream` directly:

```bash
export PYTORCH_ENABLE_MPS_FALLBACK=1          # CPU fallback for the few ops MPS lacks
export PYTORCH_MPS_HIGH_WATERMARK_RATIO=0.0   # remove the MPS allocation cap → use ALL unified memory
```

- **`PYTORCH_MPS_HIGH_WATERMARK_RATIO=0.0`** disables PyTorch's upper limit on how
  much memory the MPS allocator may hold, so a large model can use the machine's
  full unified memory pool instead of a conservative fraction.
- **`PYTORCH_ENABLE_MPS_FALLBACK=1`** lets any operation not yet implemented in
  Metal silently run on the CPU instead of crashing — important for the
  transformer‑based detectors.

The server auto‑selects the device (`mps` → `cuda` → `cpu`), so nothing else is
needed. **Verify the GPU is actually being used:**

```bash
# 1. PyTorch sees Metal:
python -c "import torch; print('MPS available:', torch.backends.mps.is_available())"
#   -> MPS available: True

# 2. The detector logs its device on startup, e.g.:
#   [YOLO] Loading yolo11n.pt on mps (imgsz=640, half=False)

# 3. Watch the GPU while streaming:
sudo powermetrics --samplers gpu_power -i 1000   # GPU active residency climbs during inference
#   (or open Activity Monitor → Window → GPU History)
```

### Performance tuning (latency levers)

Set these before launch (or in the launchd plist in [§6](#6-run-as-a-background-service-launchd)):

| Variable | Effect | Suggested |
|----------|--------|-----------|
| `QVS_YOLO_IMGSZ` | Inference input size. **Biggest** latency lever. | `480` for lower latency, `640` for accuracy |
| `QVS_YOLO_HALF` | FP16 inference on the GPU. | `true` |
| `QVS_YOLO_CONF` | Confidence threshold. | `0.5`–`0.6` |
| `QVS_YOLO_IGNORE` | Comma list of classes to drop. | e.g. `person,car,bus` |

```bash
QVS_YOLO_IMGSZ=480 QVS_YOLO_HALF=true ./run-local.sh
```

> First inference is slow (Metal shader compilation + model load). Throughput
> settles after a few frames — that's expected.

---

## 4. Run and confirm

```bash
./run-local.sh
# QuestVisionStream Server | detector=yolo | display=false
# [Health] http://0.0.0.0:8080/
# [WebRTC] Signaling on ws://0.0.0.0:3000
```

Ports:

| Port | Purpose |
|------|---------|
| **3000** | WebRTC signaling (WebSocket) — the client connects here |
| **8080** | Health endpoint (`GET /` → JSON) |

Find your Mac's LAN IP for the headset:

```bash
ipconfig getifaddr en0        # Wi‑Fi   (use en1 etc. if on a different interface)
```

On the **same Wi‑Fi**, point the WebXR client at it — no proxy, no TURN, media
flows direct:

```
https://<client-host>/?server=ws://<mac-lan-ip>:3000
```

Leave `QVS_ENABLE_TURN=false` for LAN use.

---

## 5. External access (different network than the headset)

Two things must reach the headset, and they have **different** requirements:

1. **Signaling** — a WebSocket to port 3000. Easy to proxy over HTTPS/WSS.
2. **WebRTC media** — the actual video, over ephemeral UDP. A reverse proxy does
   **not** carry this; to cross NATs it needs either a flat network (Tailscale) or
   a **TURN** relay.

Because of #2, a plain HTTPS reverse proxy alone is **not** enough for a headset
on cellular/another LAN — you also need TURN (or use Tailscale, which sidesteps
both). Pick one row:

| Method | Signaling | Media across NAT | Effort | Best for |
|--------|-----------|------------------|--------|----------|
| **Tailscale** (§5.1) | ✅ direct | ✅ direct (WireGuard) — no TURN | ⭐ lowest | Personal / a few known headsets |
| **Cloudflare Tunnel** (§5.2) + TURN | ✅ WSS, no port‑forward | ⚠️ needs TURN | medium | Public URL, no router changes |
| **Caddy reverse proxy** (§5.3) + TURN | ✅ WSS (auto‑TLS) | ⚠️ needs TURN | medium | You own a domain + can port‑forward |
| **Router port‑forward** (§5.4) + DDNS + TURN | ✅ raw/WSS | ⚠️ needs TURN | higher | Full control, static‑ish IP |

### 5.1 Tailscale (recommended — easiest and most reliable)

Tailscale puts the Mac and the headset on one flat WireGuard network, so the
headset can reach the Mac by a stable private IP **as if on the same LAN** — and
WebRTC media connects directly, **no TURN, no reverse proxy, no router changes.**

```bash
brew install tailscale        # or the Mac App Store app
sudo tailscale up
tailscale ip -4               # e.g. 100.101.102.103  (stable across networks)
```

On the Quest, install Tailscale by **sideloading its Android APK** — it is **not**
in the Meta Horizon Store (see [Quest-Headset-Setup.md](Quest-Headset-Setup.md)
for the full steps). Sign into the **same** tailnet, then open:

```
https://<client-host>/?server=ws://100.101.102.103:3000
```

Keep `QVS_ENABLE_TURN=false`. This is the recommended path for personal use.

> Tip: `tailscale serve` / Funnel can additionally expose the signaling port over
> HTTPS if your client must be served from a secure origin.

### 5.2 Cloudflare Tunnel (public URL, no port‑forwarding)

Gives a public `https://…` hostname with automatic TLS and **no inbound router
ports**. Carries the **signaling** WebSocket; pair with a TURN server (§5.5) for
media.

```bash
brew install cloudflared
cloudflared tunnel login
cloudflared tunnel create questvisionstream
# Map a DNS record to the tunnel:
cloudflared tunnel route dns questvisionstream qvs.example.com
```

Use the example config in [`examples/cloudflared-config.yml`](examples/cloudflared-config.yml):

```bash
cloudflared tunnel --config examples/cloudflared-config.yml run
```

Client URL (note **wss** — TLS is terminated by Cloudflare):

```
https://qvs.example.com/?server=wss://qvs.example.com
```

> Cloudflare proxies HTTP/WebSocket, **not** arbitrary WebRTC UDP media — so you
> still need TURN (§5.5) for the video to flow across NAT. Cloudflare's own TURN
> service, or a self‑hosted coturn, both work.

### 5.3 Caddy reverse proxy (you own a domain + can port‑forward 443)

Caddy auto‑provisions Let's Encrypt TLS and upgrades WebSockets transparently. A
ready example lives at
[`examples/Caddyfile.example`](examples/Caddyfile.example):

```bash
brew install caddy
# point qvs.example.com's A record at your public IP, forward 443 → Mac:443
caddy run --config /path/to/Caddyfile
```

Client: `https://qvs.example.com/?server=wss://qvs.example.com`. Add TURN (§5.5).

### 5.4 Router port‑forwarding + Dynamic DNS

Full control if you have a (semi‑)static public IP.

1. **Reserve** a LAN IP for the Mac (DHCP reservation in the router, keyed to the
   Mac's Wi‑Fi MAC address) so it never changes.
2. **Port‑forward** on the router: external `3000/TCP` → `<mac-lan-ip>:3000`. Do
   **not** also forward 8080 to the world — keep health internal.
3. **Dynamic DNS** if your ISP IP changes: a DDNS provider (e.g. via the router's
   built‑in DDNS, or a `ddclient` service) gives you a stable hostname.
4. Terminate TLS with Caddy (§5.3) rather than exposing raw `ws://` to the
   internet.
5. Add TURN (§5.5) for reliable media across the headset's NAT.

> Security: exposing a raw port invites scanning. Prefer Tailscale (§5.1) or a
> tunnel (§5.2). If you must port‑forward, put TLS in front, and consider IP
> allow‑lists on the router.

### 5.5 TURN (required for media on §5.2 / §5.3 / §5.4)

WebRTC uses STUN to discover a path; when both peers are behind NAT (typical for a
headset on cellular + a Mac behind a home router) it falls back to a **TURN**
relay. Configure the server to advertise your TURN server:

```bash
export QVS_ENABLE_TURN=true
export QVS_TURN_URLS="turn:your.turn.host:3478"
export QVS_TURN_USERNAME="user"
export QVS_TURN_CREDENTIAL="pass"
./run-local.sh
```

Options for the TURN server itself:
- **Self‑host coturn** (a $5 VPS is plenty): `brew`/`apt install coturn`, open
  3478/UDP+TCP.
- **Managed TURN** (Cloudflare Realtime TURN, Twilio, Metered, etc.).

Not needed on the LAN or with Tailscale (both give a direct path).

### macOS firewall

If macOS's application firewall is on, allow the Python process to accept
incoming connections: **System Settings → Network → Firewall → Options →** add/allow
your venv's `python`. Or, first time you run, click **Allow** on the prompt. The
firewall doesn't affect **outbound** tunnels (Tailscale/Cloudflare), so those need
no rule.

---

## 6. Run as a background service (launchd)

To keep the server running and auto‑start at login/boot, install the LaunchAgent
example [`examples/com.questvisionstream.plist`](examples/com.questvisionstream.plist):

```bash
# Edit paths + env in the plist first, then:
cp examples/com.questvisionstream.plist ~/Library/LaunchAgents/
launchctl load  ~/Library/LaunchAgents/com.questvisionstream.plist   # start + enable
launchctl list | grep questvisionstream                              # verify
launchctl unload ~/Library/LaunchAgents/com.questvisionstream.plist  # stop
tail -f /tmp/questvisionstream.out /tmp/questvisionstream.err        # logs
```

The plist sets the MPS environment variables and your tuning vars, so the service
uses the GPU + full unified memory exactly like `run-local.sh`.

---

## 7. Troubleshooting

| Symptom | Fix |
|---------|-----|
| `MPS available: False` | You're on an x86 Python or an old torch. Use `python@3.12` (arm64) and reinstall `torch`. `python -c "import platform;print(platform.machine())"` must say `arm64`. |
| Detector logs `on cpu` | MPS not detected (see above) or you selected `florence2` (prefers CUDA/CPU by design). |
| Headset connects, no video | Media can't traverse NAT → enable TURN (§5.5), or use Tailscale (§5.1). Confirm signaling first: `curl http://<host>:8080/`. |
| WebSocket blocked over a proxy | Ensure the proxy forwards the `Upgrade`/`Connection` headers (Caddy/Cloudflare do automatically); use `wss://` from a secure page. |
| Out‑of‑memory on large models | Lower `QVS_YOLO_IMGSZ`; the watermark var already lets you use all unified memory. |
| First frames very slow | Expected — Metal shader compile + model load. It stabilises. |
| Client can't reach `ws://` from an HTTPS page | Browsers block mixed content. Serve signaling over `wss://` (Cloudflare/Caddy/Tailscale‑serve). |

See also [`../QuestVisionStreamServer/README.md`](../QuestVisionStreamServer/README.md)
(all `QVS_*` vars) and [`Deploy-HuggingFace-Spaces.md`](Deploy-HuggingFace-Spaces.md)
(free cloud GPU).
