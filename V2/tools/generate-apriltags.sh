#!/usr/bin/env bash
#
# generate-apriltags.sh — render the printable AprilTag 36h11 test markers.
#
# Thin wrapper that runs generate-apriltags.py with a Python that has OpenCV.
# Prefers the streaming server's venv (which already installs opencv-contrib);
# falls back to python3 on PATH. Pass through any generate-apriltags.py flags,
# e.g. `--out ~/Desktop/tags` or `--tag-px 800`.
#
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

PY="$DIR/../QuestVisionStreamServer/.venv/bin/python"
if [[ ! -x "$PY" ]]; then PY="python3"; fi

if ! "$PY" -c "import cv2.aruco" >/dev/null 2>&1; then
  echo "ERROR: OpenCV (with aruco) not found for '$PY'." >&2
  echo "       Set up the server venv (run-local.sh) or 'pip install opencv-contrib-python'." >&2
  exit 1
fi

exec "$PY" "$DIR/generate-apriltags.py" "$@"
