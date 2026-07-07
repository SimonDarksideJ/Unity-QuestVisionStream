#!/usr/bin/env bash
# Native launch for Apple Silicon (Mac Mini M2) — lowest latency: uses the MPS GPU
# and unified memory directly, with media flowing headset↔Mac on the LAN.
set -euo pipefail

cd "$(dirname "$0")"

# --- venv ---
if [[ ! -d .venv ]]; then
  echo "Creating virtualenv..."
  python3 -m venv .venv
fi
# shellcheck disable=SC1091
source .venv/bin/activate

# --- deps (base + selected detector) ---
DETECTOR="${QVS_DETECTOR:-yolo}"
pip install --quiet --upgrade pip
pip install --quiet -r requirements.txt
case "$DETECTOR" in
  yolo)            pip install --quiet -r requirements-yolo.txt ;;
  owlv2|grounding_dino) pip install --quiet -r requirements-zeroshot.txt ;;
  florence2)       pip install --quiet -r requirements-florence2.txt ;;
  body)            pip install --quiet -r requirements-body.txt ;;
esac

# --- Apple Silicon MPS environment ---
export PYTORCH_ENABLE_MPS_FALLBACK=1          # fall back to CPU for unsupported ops
export PYTORCH_MPS_HIGH_WATERMARK_RATIO=0.0   # use full unified memory

# --- config (LAN direct by default) ---
export QVS_DETECTOR="$DETECTOR"
export QVS_ENABLE_DISPLAY="${QVS_ENABLE_DISPLAY:-false}"
export QVS_ENABLE_TURN="${QVS_ENABLE_TURN:-false}"
# Machine-readable detection log (one JSON line per payload sent to the client),
# for frame-for-frame comparison against a client-side capture. Set
# QVS_DETECTION_LOG to override the path, or to "" to disable.
export QVS_DETECTION_LOG="${QVS_DETECTION_LOG:-detections.jsonl}"

echo "Starting QuestVisionStream (detector=$DETECTOR)..."
exec python -m questvisionstream "$@"
