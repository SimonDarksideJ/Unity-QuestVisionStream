# Quest headset setup

Everything you do **on the Meta Quest** to run the QuestVisionStream WebXR
client. The client itself is hosted (Cloudflare Pages) — you don't install it;
you open it in the **Horizon Browser**. The one piece of real setup is getting
the headset onto the **same Tailscale tailnet** as the Mac running the server.

> Context: the client page is served over HTTPS (WebXR needs a secure context),
> so it must reach the signaling server over `wss://`. The server is exposed as
> `wss://<machine>.<tailnet>.ts.net/` by `tailscale serve` (valid Let's Encrypt
> cert). For the Quest to reach that name — and for WebRTC media to flow
> **directly, without a TURN relay** — the headset has to be on the tailnet.
> See [Install-Mac-M2.md §5.1](Install-Mac-M2.md) and
> [Configuration-and-Connectivity.md](Configuration-and-Connectivity.md).

---

## 1. Put Tailscale on the Quest

**There is no official Tailscale app in the Meta Horizon Store** — it's a
long-standing, still-open request
([tailscale/tailscale#5892](https://github.com/tailscale/tailscale/issues/5892)).
Horizon OS is Android-based, so you **sideload the Tailscale Android APK**. This
is the recommended path: it keeps media direct (no TURN — see
[§3](#3-alternative-no-app-on-the-quest)).

### 1a. Enable Developer Mode (one-time)

1. In the **Meta Horizon** phone app: **Menu → Devices →** your headset **→
   Headset settings → Developer Mode → On.** (Creating/enabling a free Meta
   developer org may be required.)
2. Reboot the headset.

### 1b. Get the APK (official source)

Download the Tailscale **Android** APK from Tailscale's own package host so you
know it's genuine:

- **<https://pkgs.tailscale.com/stable/#android>** — the stable Android APK.
- (Reference: the Play listing is
  <https://play.google.com/store/apps/details?id=com.tailscale.ipn>, but the
  Quest can't use the Play Store — grab the APK above.)

### 1c. Install it onto the headset

Any standard sideload method works (the Quest is Android):

- **SideQuest over USB** (easiest, no command line): install SideQuest on a PC,
  connect the Quest by USB-C, approve the "Allow USB debugging" prompt in the
  headset, then drag the `.apk` onto SideQuest's "install APK" button.
  Guide: <https://sidequestvr.com/setup-howto>.
- **`adb`** (if you have platform-tools): `adb install tailscale-<version>.apk`.
- **In-headset file manager** (no PC): install a sideload-capable file manager
  from the Horizon Store (e.g. AnExplorer), download the APK inside the headset,
  and open it to install.

### 1d. Sign in

1. In the headset, open **App Library → filter “Unknown Sources” → Tailscale**.
2. Sign in with the **same Tailscale account/tailnet** as the Mac.
   - If you switched the Mac to a personal tailnet to enable HTTPS certs, sign
     the Quest into that **same personal account**.
3. Toggle Tailscale **on** (the VPN key icon). Leave it running.

> Sideloaded apps open as flat 2D panels — that's expected. Tailscale just needs
> to be connected in the background; you won't interact with it during use.

---

## 2. Open the client

1. Make sure the Mac side is up: run `V2/tools/setup-tailscale-mac.sh` (starts
   the server, `tailscale serve`, and pushes the URL to Cloudflare KV).
2. In the Quest **Horizon Browser**, open the client. The GitHub deploy run's
   summary prints a big link + short code — easiest is to open that summary in
   the headset and **tap the link**, or type:
   - **Production:** short link `da.gd/qvs` (or the full `questvisionstream.pages.dev`)
3. Tap **Enter AR**. A one-time prompt — *"Stream the headset camera to
   `<host>`?"* — appears; tap **OK**.

The on-screen status panel shows what it's doing. On success:
`signaling: connected → <host>`. If it says
`signaling: can't reach <host> — no response … — retrying`, jump to
[Troubleshooting](#4-troubleshooting).

---

## 3. Alternative: no app on the Quest

If you can't enable Developer Mode / sideload on the headset, skip Tailscale on
the Quest and instead:

- Expose the signaling server publicly with **`tailscale funnel`** (still a valid
  `ts.net` HTTPS cert, no port-forward) so the Horizon Browser reaches it over
  the normal internet, **and**
- add a **TURN** relay for the WebRTC media — with the Quest off the tailnet,
  media can no longer go direct. See
  [Install-Mac-M2.md §5.5](Install-Mac-M2.md) for TURN.

Trade-off: TURN relays all video, adding latency + bandwidth cost and another
server to run. That's why sideloading Tailscale ([§1](#1-put-tailscale-on-the-quest))
is preferred for this low-latency AR use case.

---

## 4. Troubleshooting

| Symptom (status panel) | Cause / fix |
|------------------------|-------------|
| `signaling: can't reach <host> — no response — server unreachable …` (WS 1006) | No TLS listener answering. On the Mac: is `tailscale serve status` showing a `:443 → 127.0.0.1:3000` mapping? Are **HTTPS Certificates** enabled for the tailnet (admin console → DNS)? Is the Quest's Tailscale **on** and on the **same** tailnet? |
| `signaling: … TLS handshake failed …` (WS 1015) | Cert not provisioned yet — reload after a few seconds; confirm `tailscale cert <host>` succeeds on the Mac. |
| `signaling: … unauthorized …` (WS 4401) | The server has `QVS_AUTH_TOKEN` set — the client URL must carry `?token=…` (the setup script bakes it into the published URL). |
| App loads but shows `ws://localhost:3000` and never connects | The client got no server from Cloudflare — the KV/`QVS_SIGNALING_URL` isn't set for that project, or the deployment predates the `QVS_CONFIG` binding (redeploy once). Or open with `?server=wss://<host>/`. |
| Can't resolve `<host>.ts.net` on the Quest | Tailscale/MagicDNS not active on the headset — open the Tailscale app and reconnect. |

See also the connectivity matrix in
[Configuration-and-Connectivity.md](Configuration-and-Connectivity.md).
