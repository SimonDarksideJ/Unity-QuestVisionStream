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
import os
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


def configure_ffmpeg_logging(level: str) -> None:
    """Set the global libav/libswscale log threshold (PyAV).

    ``frame.to_ndarray("bgr24")`` builds a fresh libswscale context per frame,
    and each one emits a one-off ``[swscaler] No accelerated colorspace
    conversion found from yuv420p to bgr24`` at WARNING level — thousands of
    identical, benign lines that bury the real logs. Dropping the threshold to
    ``error`` (the default) suppresses these while keeping genuine ffmpeg errors.
    Best-effort: a missing/renamed API must never stop the server starting.
    """
    try:
        import av.logging as av_log

        levels = {
            "quiet": av_log.PANIC,
            "panic": av_log.PANIC,
            "fatal": av_log.FATAL,
            "error": av_log.ERROR,
            "warning": av_log.WARNING,
            "info": av_log.INFO,
            "verbose": av_log.VERBOSE,
            "debug": av_log.DEBUG,
        }
        av_log.set_level(levels.get((level or "").strip().lower(), av_log.ERROR))
    except Exception as exc:  # pragma: no cover - defensive
        print(f"[VideoProcessor] Could not set ffmpeg log level {level!r}: {exc}")


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
        # Time-throttled activity logging (independent of log_interval): the
        # inbound stream is noted every 5 s (minimal — "frames are arriving"),
        # while each detection reply is summarised every 1 s (resolution + what
        # was found), so the console mirrors what the client shows.
        self._last_recv_log = 0.0
        self._recv_window_start = 0.0
        self._recv_window_count = 0
        self._recv_window_dropped_start = 0
        self._last_sent_log = 0.0
        self._sent_window_processed_start = 0
        # Darkness/what-am-I-seeing diagnostic: set QVS_DUMP_DIR to a folder and
        # the processor writes a JPEG of the actual received frame every ~2 s
        # (filename carries the mean luma). Ground truth for "too dark" / "nothing
        # found" — the raw getUserMedia passthrough frame is often much darker
        # than the tone-mapped view you see through the headset.
        self._dump_dir = os.getenv("QVS_DUMP_DIR", "").strip()
        self._last_dump = 0.0

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

    def _dump_frame(self, img: np.ndarray, luma: float) -> None:
        """Write the received frame to QVS_DUMP_DIR for eyeball inspection."""
        try:
            import cv2

            os.makedirs(self._dump_dir, exist_ok=True)
            path = os.path.join(
                self._dump_dir, f"frame_{self.frame_count:06d}_luma{luma:03.0f}.jpg"
            )
            cv2.imwrite(path, img)
            print(f"[QVS ▣] wrote {path}")
        except Exception as exc:  # pragma: no cover - diagnostic only
            print(f"[VideoProcessor] Frame dump failed: {exc}")

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
        self._recv_window_start = loop.time()
        self._last_recv_log = loop.time()
        self._last_sent_log = loop.time()
        self._recv_window_dropped_start = self.dropped_count
        self._sent_window_processed_start = self.processed_count

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
                    self._recv_window_count += 1
                    if latest is not None:
                        self.dropped_count += 1
                    latest = frame
                    frame_ready.set()

                    # Inbound heartbeat every 5 s — just "frames are arriving".
                    now = loop.time()
                    if now - self._last_recv_log >= 5.0:
                        span = now - self._recv_window_start
                        fps = self._recv_window_count / span if span > 0 else 0.0
                        window_dropped = self.dropped_count - self._recv_window_dropped_start
                        # Per-window counts (not cumulative): how many frames actually
                        # arrived in the last ~5 s, and how many were stale-dropped.
                        print(
                            f"[QVS ↑] {frame.width}x{frame.height} — {self._recv_window_count} frames "
                            f"in {span:.1f}s @ {fps:.1f} fps ({window_dropped} stale-dropped) "
                            f"· {self.frame_count} total"
                        )
                        self._last_recv_log = now
                        self._recv_window_start = now
                        self._recv_window_count = 0
                        self._recv_window_dropped_start = self.dropped_count
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

                # Outbound reply summary every 1 s — resolution + mean luma (0..255,
                # a darkness signal) + what was found, so the console mirrors the
                # client and tells us whether the frame itself is just too dark.
                now = loop.time()
                luma = float(img.mean())
                if now - self._last_sent_log >= 1.0:
                    if detections:
                        labels = ", ".join(sorted({str(d["label"]) for d in detections}))
                        found = f"{len(detections)} found: {labels}"
                    else:
                        found = "nothing found"
                    # Frames actually run through detection in the last ~1 s (rate),
                    # not the cumulative frame index.
                    span = now - self._last_sent_log
                    window_processed = self.processed_count - self._sent_window_processed_start
                    rate = window_processed / span if span > 0 else 0.0
                    print(
                        f"[QVS ↓] {width}x{height} — {window_processed} processed "
                        f"({rate:.1f}/s) luma={luma:.0f}/255 — {found}"
                    )
                    self._last_sent_log = now
                    self._sent_window_processed_start = self.processed_count
                if self._dump_dir and now - self._last_dump >= 2.0:
                    self._dump_frame(img, luma)
                    self._last_dump = now

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
