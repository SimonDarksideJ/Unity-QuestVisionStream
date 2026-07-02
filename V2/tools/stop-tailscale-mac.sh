#!/usr/bin/env bash
#
# stop-tailscale-mac.sh — tear down what setup-tailscale-mac.sh --detach started:
# the streaming server and the Tailscale serve mount.
#
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

find_tailscale() {
  local c
  for c in tailscale /opt/homebrew/bin/tailscale /usr/local/bin/tailscale \
           /Applications/Tailscale.app/Contents/MacOS/Tailscale; do
    if command -v "$c" >/dev/null 2>&1 || [[ -x "$c" ]]; then echo "$c"; return 0; fi
  done
  return 1
}
TS="$(find_tailscale || true)"

echo "==> Stopping Tailscale serve mount"
if [[ -n "$TS" ]]; then
  "$TS" serve --bg --https=443 off >/dev/null 2>&1 || true
  echo "    done"
else
  echo "    tailscale CLI not found — skipping"
fi

echo "==> Stopping the streaming server"
# run-local.sh execs `python -m questvisionstream`.
pkill -f 'questvisionstream' 2>/dev/null && echo "    streaming server stopped" || echo "    no streaming server running"

echo "Done."
