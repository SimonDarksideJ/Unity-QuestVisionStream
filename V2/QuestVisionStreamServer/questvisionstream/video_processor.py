"""Pulls frames off the WebRTC track, runs detection, emits detection payloads.

Two properties matter here and are covered by tests:

1. **Inference never runs on the event loop.** Model forward passes are
   synchronous and can take tens of milliseconds to seconds; running them
   inline would freeze signaling, websocket keepalives, the health endpoint,
   and every other peer for the duration. Preprocess + detect run on a single
   shared worker thread (one worker also serializes access to the shared
   detector model across connections).

2. **Latest-frame-wins backpressure.** A reader task keeps draining the track
   while inference runs; only the freshest frame is processed and everything
   older is dropped. Without this, frames queue behind slow inference and the
   detection stream drifts unboundedly behind reality.

Headless-safe: the debug display is fully guarded and off by default, and runs
on the loop thread (GUI toolkits dislike worker threads).
"""
from __future__ import annotations

import asyncio
from concurrent.futures import ThreadPoolExecutor
from typing import Any, Callable, Optional

import numpy as np

from .config import ServerConfig
from .detectors import Detection, draw_detections

DetectFn = Callable[[np.ndarray], "list[Detection]"]
SendFn = Callable[[dict[str, Any]], None]

# One worker: keeps the event loop free while also serializing inference on
# the shared detector (models and stateful trackers are not thread-safe).
_inference_pool: Optional[ThreadPoolExecutor] = None


def _get_inference_pool() -> ThreadPoolExecutor:
    global _inference_pool
    if _inference_pool is None:
        _inference_pool = ThreadPoolExecutor(max_workers=1, thread_name_prefix="qvs-inference")
    return _inference_pool


class VideoProcessor:
    def __init__(self, config: ServerConfig, detect: DetectFn, send: SendFn) -> None:
        self.config = config
        self.detect = detect
        self.send = send

        self.frame_count = 0  # frames received off the track
        self.processed_count = 0  # frames that went through detection
        self.dropped_count = 0  # stale frames skipped by latest-frame-wins
        self._window_processed = 0
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

    def _preprocess_and_detect(self, frame) -> tuple[np.ndarray, "list[Detection]"] | None:
        """Runs on the inference worker thread — never on the event loop."""
        img = self._preprocess(frame)
        if img is None:
            return None
        return img, self.detect(img)

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

    def _log_window(self, loop: asyncio.AbstractEventLoop, frame) -> None:
        if self.processed_count % self.config.log_interval == 0:
            now = loop.time()
            elapsed = now - self._window_start
            fps = self._window_processed / elapsed if elapsed > 0 else 0.0
            print(
                f"[VideoProcessor] Processed {self.processed_count} "
                f"(received {self.frame_count}, dropped {self.dropped_count}) | "
                f"{frame.width}x{frame.height} | {fps:.1f} FPS"
            )
            self._window_start = now
            self._window_frames_reset()

    def _window_frames_reset(self) -> None:
        self._window_processed = 0

    async def process_video_stream(self, track) -> None:
        print("[VideoProcessor] Processing started")
        loop = asyncio.get_running_loop()
        self._window_start = loop.time()

        latest: Optional[Any] = None
        frame_ready = asyncio.Event()
        stream_ended = False

        async def read_frames() -> None:
            """Drain the track continuously; keep only the freshest frame."""
            nonlocal latest, stream_ended
            try:
                while True:
                    frame = await track.recv()
                    self.frame_count += 1
                    if latest is not None:
                        self.dropped_count += 1
                    latest = frame
                    frame_ready.set()
            except asyncio.CancelledError:
                raise
            except Exception as exc:
                print(f"[VideoProcessor] Stream ended: {exc}")
            finally:
                stream_ended = True
                frame_ready.set()  # unblock the processing loop so it can exit

        reader = asyncio.create_task(read_frames())
        try:
            while True:
                await frame_ready.wait()
                frame_ready.clear()
                frame, latest = latest, None
                if frame is None:
                    if stream_ended:
                        break
                    continue

                result = await loop.run_in_executor(
                    _get_inference_pool(), self._preprocess_and_detect, frame
                )
                if result is None:
                    continue
                img, detections = result

                self.processed_count += 1
                self._window_processed += 1
                self._log_window(loop, frame)

                height, width = img.shape[:2]
                pts = getattr(frame, "pts", None)
                self.send(
                    {
                        "type": "detections",
                        "frame": self.frame_count,
                        # Media timestamp of the processed frame (RTP clock units)
                        # so clients can correlate detections to captured frames.
                        "pts": int(pts) if pts is not None else None,
                        "width": int(width),
                        "height": int(height),
                        "detections": detections,
                    }
                )

                if not self._maybe_display(img, detections):
                    break

                if stream_ended and latest is None:
                    break
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            print(f"[VideoProcessor] Processing error: {exc}")
        finally:
            if not reader.done():
                reader.cancel()
                try:
                    await reader
                except asyncio.CancelledError:
                    pass
            self.cleanup()

    def cleanup(self) -> None:
        if self.config.enable_display and self._display_ok:
            try:
                import cv2

                cv2.destroyAllWindows()
            except Exception:
                pass
