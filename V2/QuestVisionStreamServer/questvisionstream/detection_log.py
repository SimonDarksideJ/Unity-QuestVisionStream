"""Separate, machine-readable log of every detection payload sent to a client.

The console `[QVS ↓]` line is a throttled, human-facing *summary* (deduplicated
labels, once a second). This log is the opposite: **one JSON line per payload
actually put on the data channel**, byte-faithful to what the client receives —
so a client-side capture can be diffed against it frame-for-frame.

Format: JSON Lines (one object per line). Each record is::

    {"ts": <iso8601>, "t_mono": <float>, "client": <str>, "count": <int>,
     "payload": {"type": "detections", "frame": .., "pts": .., "width": ..,
                 "height": .., "detections": [{"label","conf","bbox"}, ..]}}

``payload`` is the exact dict serialized onto the wire; the surrounding fields
are server-side metadata (send time + which client) for correlation. Fresh per
server run (truncated on first write), mirroring how ``server.log`` is recreated
each launch. Fully best-effort: any file error disables the log and is reported
once — it must never disrupt a live session.
"""
from __future__ import annotations

import json
import os
import threading
from datetime import datetime, timezone
from typing import Any, Optional


class DetectionLog:
    def __init__(self, path: str) -> None:
        self.path = path
        self._fh = None
        self._failed = False
        # Writes happen on the event-loop thread today; the lock keeps the file
        # safe if that ever changes and costs nothing under no contention.
        self._lock = threading.Lock()

    def _ensure_open(self) -> None:
        if self._fh is not None or self._failed:
            return
        try:
            directory = os.path.dirname(self.path)
            if directory:
                os.makedirs(directory, exist_ok=True)
            # "w": one clean file per server process (all connections in this run
            # append to the open handle); buffering=1 → line-buffered, so a live
            # `tail -f` sees each detection as it is sent.
            self._fh = open(self.path, "w", buffering=1, encoding="utf-8")
            print(f"[DetectionLog] writing detection payloads to {self.path}")
        except Exception as exc:
            self._failed = True
            print(f"[DetectionLog] disabled — cannot open {self.path}: {exc}")

    def log(self, client: str, payload: dict[str, Any]) -> None:
        """Append one record for a payload just sent to ``client``."""
        if self._failed:
            return
        detections = payload.get("detections") or []
        record = {
            "ts": datetime.now(timezone.utc).isoformat(),
            # Monotonic clock: comparable across records for ordering/latency,
            # immune to wall-clock jumps (NTP, DST) mid-session.
            "t_mono": _monotonic(),
            "client": client,
            "count": len(detections),
            "payload": payload,
        }
        with self._lock:
            self._ensure_open()
            if self._fh is None:
                return
            try:
                self._fh.write(json.dumps(record, separators=(",", ":")) + "\n")
            except Exception as exc:  # pragma: no cover - defensive
                print(f"[DetectionLog] write failed: {exc}")

    def close(self) -> None:
        with self._lock:
            if self._fh is not None:
                try:
                    self._fh.close()
                finally:
                    self._fh = None


def _monotonic() -> float:
    import time

    return time.monotonic()


def create_detection_log(path: str) -> Optional[DetectionLog]:
    """A logger for a non-empty path, else ``None`` (feature disabled)."""
    path = (path or "").strip()
    return DetectionLog(path) if path else None
