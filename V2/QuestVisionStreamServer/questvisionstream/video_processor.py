"""Pulls frames off the WebRTC track, runs detection, emits detection payloads.

Headless-safe: the debug display is fully guarded and off by default. Detection
drawing happens here (only when displaying), not on the detector hot path.
"""
from __future__ import annotations

import asyncio
from typing import Any, Callable

import numpy as np

from .config import ServerConfig
from .detectors import Detection, draw_detections

DetectFn = Callable[[np.ndarray], "list[Detection]"]
SendFn = Callable[[dict[str, Any]], None]


class VideoProcessor:
    def __init__(self, config: ServerConfig, detect: DetectFn, send: SendFn) -> None:
        self.config = config
        self.detect = detect
        self.send = send

        self.frame_count = 0
        self._window_frames = 0
        self._window_start = 0.0
        self._display_ok = True

    def _preprocess(self, frame) -> np.ndarray | None:
        try:
            img = frame.to_ndarray(format="bgr24")
        except Exception as exc:  # pragma: no cover - defensive
            print(f"[VideoProcessor] Frame decode error: {exc}")
            return None
        if img is None or img.size == 0:
            return None

        import cv2

        if self.config.rotate_180:
            img = cv2.rotate(img, cv2.ROTATE_180)
        else:
            if self.config.flip_vertical:
                img = cv2.flip(img, 0)
            if self.config.flip_horizontal:
                img = cv2.flip(img, 1)
        return img

    def _maybe_display(self, img: np.ndarray, detections: "list[Detection]") -> bool:
        if not self.config.enable_display or not self._display_ok:
            return True
        try:
            import cv2

            draw_detections(img, detections)
            cv2.imshow("QuestVisionStream", img)
            return (cv2.waitKey(1) & 0xFF) != ord("q")
        except Exception as exc:
            # opencv-python-headless has no GUI — disable display and carry on.
            print(f"[VideoProcessor] Display unavailable, disabling: {exc}")
            self._display_ok = False
            return True

    async def process_video_stream(self, track) -> None:
        print("[VideoProcessor] Processing started")
        loop = asyncio.get_event_loop()
        self._window_start = loop.time()
        try:
            while True:
                frame = await track.recv()
                self.frame_count += 1
                self._window_frames += 1

                if self.frame_count % self.config.log_interval == 0:
                    now = loop.time()
                    elapsed = now - self._window_start
                    fps = self._window_frames / elapsed if elapsed > 0 else 0.0
                    print(
                        f"[VideoProcessor] Frame {self.frame_count} | "
                        f"{frame.width}x{frame.height} | {fps:.1f} FPS"
                    )
                    self._window_start = now
                    self._window_frames = 0

                img = self._preprocess(frame)
                if img is None:
                    continue

                detections = self.detect(img)
                height, width = img.shape[:2]
                self.send(
                    {
                        "type": "detections",
                        "frame": self.frame_count,
                        "width": int(width),
                        "height": int(height),
                        "detections": detections,
                    }
                )

                if not self._maybe_display(img, detections):
                    break
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            print(f"[VideoProcessor] Stream ended: {exc}")
        finally:
            self.cleanup()

    def cleanup(self) -> None:
        if self.config.enable_display and self._display_ok:
            try:
                import cv2

                cv2.destroyAllWindows()
            except Exception:
                pass
