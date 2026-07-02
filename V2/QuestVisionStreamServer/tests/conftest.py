"""Shared test fakes for the QuestVisionStream server suite.

Everything here is dependency-light: fake frames/tracks stand in for aiortc
media objects so the pipeline can be exercised without a real WebRTC session,
and the fake websocket drives ``handle_signaling`` directly.
"""
from __future__ import annotations

import asyncio
import fractions
from dataclasses import dataclass, field

import numpy as np
import pytest


def make_config(**env_overrides):
    """Build a ServerConfig with QVS_* env overrides applied for the call."""
    import os

    from questvisionstream.config import load_config

    saved = {k: os.environ.get(k) for k in env_overrides}
    os.environ.update({k: str(v) for k, v in env_overrides.items()})
    try:
        return load_config()
    finally:
        for k, v in saved.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v


@dataclass
class FakeFrame:
    """Stands in for an av.VideoFrame coming off the WebRTC track."""

    index: int
    width: int = 64
    height: int = 48
    pts: int | None = None
    time_base: fractions.Fraction | None = fractions.Fraction(1, 90000)

    def to_ndarray(self, format: str = "bgr24") -> np.ndarray:  # noqa: A002
        return np.zeros((self.height, self.width, 3), dtype=np.uint8)


class StreamEnded(Exception):
    """Raised by fake tracks when the stream is exhausted (like MediaStreamError)."""


class ScriptedTrack:
    """Yields a fixed number of frames, one per ``interval_s``. Pace it slower
    than the detector under test when the test needs every frame processed
    (latest-frame-wins drops intermediates from a faster-than-detect feed)."""

    kind = "video"

    def __init__(self, n_frames: int, pts_step: int = 3000, interval_s: float = 0.0) -> None:
        self.n_frames = n_frames
        self.pts_step = pts_step
        self.interval_s = interval_s
        self.served = 0

    async def recv(self) -> FakeFrame:
        if self.served >= self.n_frames:
            raise StreamEnded("end of scripted stream")
        frame = FakeFrame(index=self.served, pts=self.served * self.pts_step)
        self.served += 1
        await asyncio.sleep(self.interval_s)  # yield to the loop like a real recv
        return frame


class PacedBufferedTrack:
    """Produces frames at a fixed real-time rate into a FIFO, like aiortc's
    jitter buffer: ``recv()`` returns the OLDEST buffered frame. This is what
    exposes backpressure — if the consumer is slower than the producer, the
    buffer (and end-to-end lag) grows without bound.
    """

    kind = "video"

    def __init__(self, n_frames: int, interval_s: float, pts_step: int = 3000) -> None:
        self.n_frames = n_frames
        self.interval_s = interval_s
        self.pts_step = pts_step
        self.produced = 0
        self.buffer: asyncio.Queue[FakeFrame] = asyncio.Queue()
        self.arrival_time: dict[int, float] = {}
        self._producer: asyncio.Task | None = None

    def start(self) -> None:
        self._producer = asyncio.get_running_loop().create_task(self._produce())

    async def _produce(self) -> None:
        loop = asyncio.get_running_loop()
        for i in range(self.n_frames):
            frame = FakeFrame(index=i, pts=i * self.pts_step)
            self.arrival_time[i] = loop.time()
            self.buffer.put_nowait(frame)
            self.produced += 1
            await asyncio.sleep(self.interval_s)

    async def recv(self) -> FakeFrame:
        if self.produced >= self.n_frames and self.buffer.empty():
            raise StreamEnded("end of paced stream")
        return await self.buffer.get()

    async def stop(self) -> None:
        if self._producer:
            self._producer.cancel()
            try:
                await self._producer
            except asyncio.CancelledError:
                pass


class LoopStallMonitor:
    """Measures the longest stretch the event loop went unserviced.

    A well-behaved async server should keep this in the low milliseconds even
    while inference is running; blocking calls on the loop show up directly.
    """

    def __init__(self, tick_s: float = 0.005) -> None:
        self.tick_s = tick_s
        self.max_stall_s = 0.0
        self._task: asyncio.Task | None = None

    async def _run(self) -> None:
        loop = asyncio.get_running_loop()
        last = loop.time()
        while True:
            await asyncio.sleep(self.tick_s)
            now = loop.time()
            stall = now - last - self.tick_s
            if stall > self.max_stall_s:
                self.max_stall_s = stall
            last = now

    def start(self) -> None:
        self._task = asyncio.get_running_loop().create_task(self._run())

    async def stop(self) -> float:
        assert self._task is not None
        self._task.cancel()
        try:
            await self._task
        except asyncio.CancelledError:
            pass
        return self.max_stall_s


@dataclass
class SentMessage:
    data: str


class FakeWebSocket:
    """Drives handle_signaling: an async-iterable message source + send/close sink."""

    def __init__(self, request_headers: dict[str, str] | None = None, path: str = "/") -> None:
        self.inbound: asyncio.Queue[str | None] = asyncio.Queue()
        self.sent: list[str] = []
        self.closed = False
        self.close_code: int | None = None
        self.path = path
        self.request_headers = request_headers or {}

    def feed(self, message: str) -> None:
        self.inbound.put_nowait(message)

    def end(self) -> None:
        """Terminate the async iteration (client went away)."""
        self.inbound.put_nowait(None)

    async def send(self, data: str) -> None:
        self.sent.append(data)

    async def close(self, code: int = 1000, reason: str = "") -> None:
        self.closed = True
        self.close_code = code
        self.end()

    def __aiter__(self):
        return self

    async def __anext__(self) -> str:
        item = await self.inbound.get()
        if item is None:
            raise StopAsyncIteration
        return item


class MessageCountingWebSocket(FakeWebSocket):
    """Counts how many inbound messages the handler actually consumed."""

    def __init__(self, messages: list[str], **kwargs) -> None:
        super().__init__(**kwargs)
        self.consumed = 0
        for m in messages:
            self.feed(m)
        self.end()

    async def __anext__(self) -> str:
        item = await super().__anext__()
        self.consumed += 1
        return item


@pytest.fixture
def collected_payloads() -> list[dict]:
    return []
