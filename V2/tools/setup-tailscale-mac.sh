#!/usr/bin/env bash
#
# setup-tailscale-mac.sh — set up + run the QuestVisionStream (V2) streaming
# server on a Mac (Apple Silicon) and expose its signaling endpoint over
# Tailscale with valid HTTPS/WSS.
#
# Scope
# -----
# This script runs ONLY the streaming service (the Python signaling + WebRTC
# server) and publishes its address. It does NOT build or serve the WebXR
# client — that is hosted separately (Cloudflare Pages) and discovers this
# server at runtime via its `/api/config` → QVS_SIGNALING_URL value.
#
# What it produces
# ----------------
# A single signaling URL to paste into the Cloudflare KV / Pages env var
# `QVS_SIGNALING_URL` (see V2/quest-client/src/config.ts):
#
#     wss://<machine>.<tailnet>.ts.net/
#
# Why Tailscale + WSS: the Quest's WebXR client page is served over HTTPS, so its
# signaling socket must be wss:// (an ws:// from an https:// page is blocked as
# mixed content). Tailscale `serve` fronts the local ws:// server with an
# auto-provisioned Let's Encrypt cert on the tailnet MagicDNS name. WebRTC media
# then flows directly over WireGuard (no TURN) once the Quest is on the same
# tailnet.
#
# The repo lives on external storage, so we also strip macOS quarantine and fix
# permissions before running anything.
#
# Usage:
#     ./setup-tailscale-mac.sh            # set up, run, print the KV URL, wait (Ctrl-C tears down)
#     ./setup-tailscale-mac.sh --detach   # same, but leave it running and exit
#     ./setup-tailscale-mac.sh --reset     # clear this machine's Tailscale serve config first
#
# Cloudflare KV auto-update
# --------------------------
# If a KV namespace id is known (baked default below, or QVS_CF_KV_NAMESPACE_ID)
# and wrangler is authenticated, the script pushes the signaling URL into the
# `signaling_url` key so the hosted client picks it up live (via /api/config).
# The one-time QVS_CONFIG *binding* on each Pages project must already exist (it
# activates on that project's next deploy); after that, value pushes are live.
# Disable with --no-push-kv.
#
# Honored env vars (all optional; passed through to the server):
#     QVS_DETECTOR (default yolo), QVS_YOLO_IMGSZ, QVS_YOLO_HALF, QVS_YOLO_CONF,
#     QVS_AUTH_TOKEN (appended to the published URL when set),
#     SIGNAL_PORT (default 3000), QVS_HEALTH_PORT (default 8080),
#     QVS_CF_KV_NAMESPACE_ID (Cloudflare KV namespace bound as QVS_CONFIG)
#
set -euo pipefail

# --------------------------------------------------------------------------- #
#  Paths & constants
# --------------------------------------------------------------------------- #
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
V2_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
SERVER_DIR="$V2_DIR/QuestVisionStreamServer"
RUN_DIR="$SCRIPT_DIR/.run"                 # logs + pid files (gitignored)
mkdir -p "$RUN_DIR"

SIGNAL_PORT="${SIGNAL_PORT:-3000}"         # Python signaling WebSocket (local)
HEALTH_PORT="${QVS_HEALTH_PORT:-8080}"     # Python health endpoint (local only)
# KV namespace bound as QVS_CONFIG on the Pages projects (created during setup).
CF_KV_NAMESPACE_ID="${QVS_CF_KV_NAMESPACE_ID:-dadee36b3126445fb6d0b1ca0620402e}"

DETACH=false
RESET=false
PUSH_KV=true
for arg in "$@"; do
  case "$arg" in
    --detach)     DETACH=true ;;
    --reset)      RESET=true ;;
    --no-push-kv) PUSH_KV=false ;;
    -h|--help)    grep '^#' "$0" | sed 's/^#\s\?//'; exit 0 ;;
    *) echo "Unknown option: $arg (try --help)" >&2; exit 2 ;;
  esac
done

# --------------------------------------------------------------------------- #
#  Pretty output helpers
# --------------------------------------------------------------------------- #
if [[ -t 1 ]]; then
  B=$'\033[1m'; DIM=$'\033[2m'; G=$'\033[32m'; Y=$'\033[33m'; R=$'\033[31m'; C=$'\033[36m'; Z=$'\033[0m'
else
  B=''; DIM=''; G=''; Y=''; R=''; C=''; Z=''
fi
step() { printf '\n%s==>%s %s%s%s\n' "$C" "$Z" "$B" "$1" "$Z"; }
info() { printf '    %s\n' "$1"; }
ok()   { printf '    %s✓%s %s\n' "$G" "$Z" "$1"; }
warn() { printf '    %s!%s %s\n' "$Y" "$Z" "$1"; }
die()  { printf '\n%sERROR:%s %s\n' "$R" "$Z" "$1" >&2; exit 1; }

# --------------------------------------------------------------------------- #
#  0. Locate the Tailscale CLI (Homebrew or the Mac App Store app)
# --------------------------------------------------------------------------- #
find_tailscale() {
  local c
  for c in tailscale /opt/homebrew/bin/tailscale /usr/local/bin/tailscale \
           /Applications/Tailscale.app/Contents/MacOS/Tailscale; do
    if command -v "$c" >/dev/null 2>&1 || [[ -x "$c" ]]; then echo "$c"; return 0; fi
  done
  return 1
}
is_brew_tailscale() {
  command -v brew >/dev/null 2>&1 && brew list --formula 2>/dev/null | grep -qx tailscale
}

# --------------------------------------------------------------------------- #
#  1. Prerequisites  (server only — no node/npm needed)
# --------------------------------------------------------------------------- #
step "Checking prerequisites"
[[ "$(uname -s)" == "Darwin" ]] || die "This script targets macOS."
command -v python3 >/dev/null 2>&1 || die "python3 not found. Install: brew install python@3.12"
ok "python3 $(python3 -c 'import platform;print(platform.python_version())')"
[[ "$(python3 -c 'import platform;print(platform.machine())')" == "arm64" ]] \
  || warn "python3 is not arm64 — MPS GPU will be unavailable (see Install-Mac-M2.md §7)."
TS="$(find_tailscale)" || die "Tailscale not found. Install: brew install tailscale  (or the Mac App Store app)."
ok "tailscale: $TS"

# --------------------------------------------------------------------------- #
#  2. External storage: quarantine + permissions
# --------------------------------------------------------------------------- #
step "Preparing external-storage checkout ($V2_DIR)"
if [[ "$V2_DIR" == /Volumes/* ]]; then
  VOL="/Volumes/$(printf '%s' "${V2_DIR#/Volumes/}" | cut -d/ -f1)"
  info "On external volume: $VOL"
  if mount | grep -F " on $VOL " | grep -qw noexec; then
    die "Volume $VOL is mounted 'noexec' — the venv/binaries can't run from it. Remount without noexec."
  fi
  if ! ( : > "$SERVER_DIR/.qvs_write_test" ) 2>/dev/null; then
    die "No write permission in $SERVER_DIR. In Finder → Get Info on $VOL, tick 'Ignore ownership on this volume'."
  fi
  rm -f "$SERVER_DIR/.qvs_write_test"
  ok "Volume is writable and executable"
else
  info "Not under /Volumes — treating as internal storage."
fi

# Strip Gatekeeper quarantine so cloned scripts/binaries run without prompts.
QCOUNT="$(xattr -rl "$SERVER_DIR" 2>/dev/null | grep -c 'com.apple.quarantine' || true)"
if [[ "${QCOUNT:-0}" -gt 0 ]]; then
  info "Clearing com.apple.quarantine on ~$QCOUNT item(s)…"
  xattr -dr com.apple.quarantine "$SERVER_DIR" 2>/dev/null || warn "Could not clear all quarantine attrs (non-fatal)."
  ok "Quarantine cleared"
else
  ok "No quarantine attributes present"
fi
chmod +x "$SERVER_DIR/run-local.sh" "$SCRIPT_DIR"/*.sh 2>/dev/null || true
ok "Scripts marked executable"

# --------------------------------------------------------------------------- #
#  3. Tailscale: ensure daemon + login, then resolve this machine's name
# --------------------------------------------------------------------------- #
step "Resolving Tailscale identity"

# (a) Is the tailscaled DAEMON running? Homebrew ships it but does NOT auto-start
#     it — the CLI then reports "failed to connect to local Tailscale service".
status_out="$("$TS" status 2>&1 || true)"
if grep -qi "failed to connect to local Tailscale service" <<<"$status_out"; then
  warn "The Tailscale daemon (tailscaled) isn't running."
  if is_brew_tailscale; then
    info "Starting it via Homebrew (needs sudo)…"
    sudo brew services start tailscale \
      || die "Could not start tailscaled. Run: sudo brew services start tailscale"
  elif [[ -d /Applications/Tailscale.app ]]; then
    info "Launching the Tailscale app…"; open -a Tailscale || true
  else
    die "Start the Tailscale daemon and re-run. (Homebrew: sudo brew services start tailscale)"
  fi
  info "Waiting for the daemon socket…"
  for _ in $(seq 1 15); do
    status_out="$("$TS" status 2>&1 || true)"
    grep -qi "failed to connect to local Tailscale service" <<<"$status_out" || break
    sleep 1
  done
fi

# (b) Daemon up — logged in AND connected? --operator lets THIS user run
#     status/serve later without sudo (Homebrew's socket is otherwise root-only).
if ! "$TS" status >/dev/null 2>&1; then
  warn "Not connected to a tailnet. Running 'tailscale up' (may open a browser for login)…"
  sudo "$TS" up --operator="$USER" \
    || die "Could not bring Tailscale up. Run 'sudo $TS up --operator=$USER' and re-run this script."
fi

FQDN="$("$TS" status --json 2>/dev/null | python3 -c '
import json,sys
try:
    d=json.load(sys.stdin)
    print((d.get("Self") or {}).get("DNSName","").rstrip("."))
except Exception:
    print("")
')"
[[ -n "$FQDN" ]] || die "Could not read this machine'\''s MagicDNS name. Is Tailscale logged in? Try: $TS status"
ok "MagicDNS name: $FQDN"

# The published signaling URL (served at the HTTPS root, port 443).
SIGNAL_URL="wss://$FQDN/"
if [[ -n "${QVS_AUTH_TOKEN:-}" ]]; then
  SIGNAL_URL="wss://$FQDN/?token=$QVS_AUTH_TOKEN"
  info "Auth token enabled — appended to the published URL."
fi

# --------------------------------------------------------------------------- #
#  4. Start the streaming server
# --------------------------------------------------------------------------- #
SERVER_PID=""
cleanup() {
  step "Shutting down"
  [[ -n "$SERVER_PID" ]] && kill "$SERVER_PID" 2>/dev/null || true
  "$TS" serve --bg --https=443 off >/dev/null 2>&1 || true
  ok "Stopped the server and removed the Tailscale serve mount"
}
wait_for() {  # wait_for <url> <label> <max_seconds>
  local url="$1" label="$2" max="${3:-40}" i=0
  until curl -fsS -o /dev/null "$url" 2>/dev/null; do
    i=$((i+1)); [[ $i -ge $max ]] && return 1
    sleep 1
  done
  ok "$label is up"
}

step "Starting the streaming server (detector=${QVS_DETECTOR:-yolo})"
cd "$SERVER_DIR"
# run-local.sh creates the venv, installs deps, sets the MPS env, and execs the server.
QVS_PORT="$SIGNAL_PORT" QVS_HEALTH_PORT="$HEALTH_PORT" \
  nohup ./run-local.sh > "$RUN_DIR/server.log" 2>&1 &
SERVER_PID=$!
info "server.log → $RUN_DIR/server.log (pid $SERVER_PID)"

if $DETACH; then trap - INT TERM EXIT; else trap cleanup INT TERM EXIT; fi

info "Waiting for the model to load + health endpoint (first run downloads weights)…"
wait_for "http://127.0.0.1:$HEALTH_PORT/" "Streaming server" 180 \
  || die "Server never became healthy — see $RUN_DIR/server.log"

# --------------------------------------------------------------------------- #
#  5. Expose the signaling endpoint over HTTPS/WSS via Tailscale serve
# --------------------------------------------------------------------------- #
step "Exposing signaling over HTTPS/WSS via Tailscale serve"
if $RESET; then
  info "Resetting existing serve config…"; "$TS" serve reset >/dev/null 2>&1 || true
fi
"$TS" serve --bg --https=443 "http://127.0.0.1:$SIGNAL_PORT" \
  || die "tailscale serve failed. Ensure HTTPS certs are enabled for your tailnet (admin console → DNS → HTTPS Certificates)."
ok "Signaling served at $SIGNAL_URL"

# Warm the cert so the first client connection doesn't stall on provisioning.
curl -fsS -o /dev/null --max-time 25 "https://$FQDN/" 2>/dev/null || true

# --------------------------------------------------------------------------- #
#  6. Push the URL into Cloudflare KV (live client config), if wired up
# --------------------------------------------------------------------------- #
KV_PUSHED=false
if $PUSH_KV && [[ -n "$CF_KV_NAMESPACE_ID" ]]; then
  step "Updating Cloudflare KV (signaling_url)"
  if command -v npx >/dev/null 2>&1 && npx --yes wrangler@latest whoami >/dev/null 2>&1; then
    if npx --yes wrangler@latest kv key put --namespace-id="$CF_KV_NAMESPACE_ID" \
         signaling_url "$SIGNAL_URL" --remote >/dev/null 2>&1; then
      ok "KV signaling_url → $SIGNAL_URL  (ns $CF_KV_NAMESPACE_ID)"
      KV_PUSHED=true
    else
      warn "wrangler kv put failed — set it manually (see banner). "
    fi
  else
    warn "wrangler not authenticated ('npx wrangler login') — skipping live KV update."
  fi
fi

# --------------------------------------------------------------------------- #
#  7. Final banner — the URL for the Cloudflare KV registry
# --------------------------------------------------------------------------- #
cat <<BANNER

${B}${G}════════════════════════════════════════════════════════════════${Z}
${B}  QuestVisionStream signaling is live over Tailscale (WSS)${Z}
${B}${G}════════════════════════════════════════════════════════════════${Z}

  Signaling URL (Cloudflare KV ${B}QVS_CONFIG${Z} / key ${B}signaling_url${Z}):

      ${B}${C}$SIGNAL_URL${Z}

$( $KV_PUSHED \
   && printf '  %s✓%s Pushed to Cloudflare KV live — reload the client to pick it up.\n' "$G" "$Z" \
   || printf '  Set it in KV manually:\n      npx wrangler kv key put --namespace-id=%s signaling_url "%s" --remote\n' "$CF_KV_NAMESPACE_ID" "$SIGNAL_URL" )

  The Quest opens your Cloudflare-hosted client normally; it will then
  connect signaling to the URL above. Media flows direct over WireGuard —
  make sure the Quest is signed into the ${B}same tailnet${Z}.

  ${DIM}Local server : http://127.0.0.1:$SIGNAL_PORT  (proxied → $SIGNAL_URL)${Z}
  ${DIM}Health       : http://127.0.0.1:$HEALTH_PORT/  (local only)${Z}
  ${DIM}Serve status : $TS serve status${Z}
  ${DIM}Logs         : $RUN_DIR/server.log${Z}

BANNER

if $DETACH; then
  cat <<'MSG'
  Running detached. To stop later:

      ./stop-tailscale-mac.sh

MSG
  exit 0
fi

info "Press Ctrl-C to stop the server and remove the Tailscale serve mount."
while kill -0 "$SERVER_PID" 2>/dev/null; do sleep 2; done
warn "The server process exited — check $RUN_DIR/server.log."
