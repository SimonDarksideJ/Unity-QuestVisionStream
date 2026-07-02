"""VideoProcessor pipeline tests: event-loop responsiveness, backpressure,
frame correlation, and config-hardening around the frame loop.

The stats printed by the two PERF tests (max loop stall / end-to-end lag) are
the before/after evidence for the improvement guide.
"""
from __future__ import annotations

import asyncio
import time

import pytest

from questvisionstream.video_processor import VideoProcessor

from conftest import (
    LoopStallMonitor,
    PacedBufferedTrack,
    ScriptedTrack,
    make_config,
)

INFERENCE_S = 0.10  # simulated model forward-pass time


def blocking_detect_factory(inference_s: float = INFERENCE_S, calls: list | None = None):
    """A detector stand-in that burns wall-clock time like a real forward pass."""

    def detect(img):
        time.sleep(inference_s)
        if calls is not None:
            calls.append(time.monotonic())
        return [{"label": "cup", "conf": 0.9, "bbox": [1.0, 2.0, 3.0, 4.0]}]

    return detect


@pytest.mark.asyncio
async def test_inference_does_not_stall_event_loop(collected_payloads):
    """S1: while a frame is being inferred, the asyncio loop must keep serving
    other work (signaling, keepalives, health, other peers).

    With inference inline on the loop, max stall ~= inference time (100ms here).
    Off-loop inference should keep the stall well under 25ms.
    """
    config = make_config(QVS_ENABLE_DISPLAY="false")
    n_frames = 8
    # Paced slower than inference so every frame is processed (no drops) and
    # the monitor observes the loop across 8 consecutive forward passes.
    track = ScriptedTrack(n_frames, interval_s=INFERENCE_S * 1.2)
    processor = VideoProcessor(config, blocking_detect_factory(), collected_payloads.append)

    monitor = LoopStallMonitor()
    monitor.start()
    await processor.process_video_stream(track)
    max_stall = await monitor.stop()

    print(f"\n[STATS] max event-loop stall during {n_frames}x{INFERENCE_S * 1000:.0f}ms "
          f"inference: {max_stall * 1000:.1f}ms")
    assert len(collected_payloads) == n_frames
    assert max_stall < 0.025, (
        f"event loop stalled {max_stall * 1000:.1f}ms — inference is blocking the loop"
    )


@pytest.mark.asyncio
async def test_slow_inference_drops_stale_frames_not_freshness(collected_payloads):
    """Backpressure: when frames arrive faster than inference runs, the
    processor must skip stale frames and stay near the live edge, not queue
    them. We measure the end-to-end lag (arrival -> payload sent) of the last
    processed frame; FIFO processing makes it grow with stream length,
    latest-frame-wins keeps it around one inference interval.
    """
    config = make_config(QVS_ENABLE_DISPLAY="false")
    n_frames, frame_interval = 40, 0.01  # 100 fps feed
    inference_s = 0.05  # 20 fps detector -> 5x oversubscribed
    track = PacedBufferedTrack(n_frames, frame_interval)

    loop = asyncio.get_running_loop()
    sent_at: dict[int, float] = {}

    def send(payload: dict) -> None:
        collected_payloads.append(payload)
        sent_at[len(collected_payloads) - 1] = loop.time()

    processor = VideoProcessor(config, blocking_detect_factory(inference_s), send)
    track.start()
    await processor.process_video_stream(track)
    await track.stop()

    # Which source frame did each payload come from? pts is the ground truth.
    last_payload = collected_payloads[-1]
    last_src_index = last_payload["pts"] // 3000 if last_payload.get("pts") is not None else None
    assert last_src_index is not None, "payload must carry pts for frame correlation"

    lag_s = sent_at[len(collected_payloads) - 1] - track.arrival_time[last_src_index]
    processed = len(collected_payloads)
    print(f"\n[STATS] {n_frames} frames @ {1 / frame_interval:.0f}fps into a "
          f"{1 / inference_s:.0f}fps detector: processed={processed}, "
          f"last-frame end-to-end lag={lag_s * 1000:.0f}ms")

    # FIFO processes all 40 frames and the tail lag approaches
    # n_frames * (inference - interval) ≈ 1.6s. Latest-wins should be < 3
    # inference intervals and must NOT have processed every frame.
    assert processed < n_frames, "every frame was processed — no stale-frame dropping"
    assert lag_s < inference_s * 3, f"end-to-end lag {lag_s * 1000:.0f}ms — falling behind live"


@pytest.mark.asyncio
async def test_payload_carries_media_pts(collected_payloads):
    """Detections must be correlatable to the captured frame: the payload
    needs the media timestamp (pts), not just a server-side counter."""
    config = make_config(QVS_ENABLE_DISPLAY="false")
    track = ScriptedTrack(3, pts_step=3000, interval_s=0.02)
    processor = VideoProcessor(config, blocking_detect_factory(0.0), collected_payloads.append)
    await processor.process_video_stream(track)

    assert collected_payloads, "no payloads emitted"
    for payload in collected_payloads:
        assert "pts" in payload, "payload missing media pts"
    assert [p["pts"] for p in collected_payloads] == [0, 3000, 6000]
    # Wire compat: the existing fields must still be present and well-typed.
    first = collected_payloads[0]
    assert first["type"] == "detections"
    assert isinstance(first["width"], int) and isinstance(first["height"], int)
    assert isinstance(first["frame"], int)


@pytest.mark.asyncio
async def test_log_interval_zero_does_not_kill_stream(collected_payloads):
    """S4: QVS_LOG_INTERVAL=0 must not crash the frame loop (ZeroDivisionError)."""
    config = make_config(QVS_ENABLE_DISPLAY="false", QVS_LOG_INTERVAL="0")
    n_frames = 5
    track = ScriptedTrack(n_frames, interval_s=0.02)
    processor = VideoProcessor(config, blocking_detect_factory(0.0), collected_payloads.append)
    await processor.process_video_stream(track)

    assert len(collected_payloads) == n_frames, (
        f"stream died after {len(collected_payloads)} frames with QVS_LOG_INTERVAL=0"
    )
