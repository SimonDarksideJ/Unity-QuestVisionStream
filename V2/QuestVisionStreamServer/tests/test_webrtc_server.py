"""Signaling-layer tests: message robustness, session security (auth token,
origin allowlist, connection cap/eviction), and track-task lifecycle.
"""
from __future__ import annotations

import asyncio
import json

import pytest

from questvisionstream.video_processor import VideoProcessor
from questvisionstream.webrtc_server import WebRTCServer

from conftest import FakeWebSocket, MessageCountingWebSocket, make_config


def make_server(config) -> WebRTCServer:
    def make_processor() -> VideoProcessor:
        return VideoProcessor(config, lambda img: [], lambda payload: None)

    return WebRTCServer(config, make_processor)


class HangingTrack:
    """recv() blocks until the task is cancelled; records the cancellation."""

    kind = "video"

    def __init__(self) -> None:
        self.cancelled = False

    async def recv(self):
        try:
            await asyncio.Event().wait()
        except asyncio.CancelledError:
            self.cancelled = True
            raise


@pytest.mark.asyncio
async def test_malformed_signaling_messages_do_not_tear_down_session():
    """A single bad message must be skipped, not kill the whole connection."""
    config = make_config()
    server = make_server(config)
    messages = [
        "not json at all",
        json.dumps({"no": "type"}),
        json.dumps({"type": 123}),
        json.dumps({"type": "candidate"}),  # missing candidate/sdpMid/sdpMLineIndex
        json.dumps({"type": "candidate", "candidate": "bogus", "sdpMid": "0", "sdpMLineIndex": "x"}),
        json.dumps({"type": "future-extension"}),
    ]
    ws = MessageCountingWebSocket(messages)
    await asyncio.wait_for(server.handle_signaling(ws), timeout=5)

    assert ws.consumed == len(messages), (
        f"handler consumed {ws.consumed}/{len(messages)} messages — "
        "a malformed message tore down the session"
    )
    assert len(server.pcs) == 0, "peer connection leaked after session end"


@pytest.mark.asyncio
async def test_auth_token_rejects_unauthenticated_connection():
    """With QVS_AUTH_TOKEN set, a connection without the token must be
    rejected before any peer connection is created."""
    config = make_config(QVS_AUTH_TOKEN="s3cret")
    assert getattr(config, "auth_token", None) == "s3cret", "config lacks auth_token"
    server = make_server(config)

    ws = FakeWebSocket(path="/")
    ws.feed(json.dumps({"type": "future-extension"}))
    ws.end()
    await asyncio.wait_for(server.handle_signaling(ws), timeout=5)
    assert ws.closed and ws.close_code == 4401, "unauthenticated connection was accepted"

    ok = FakeWebSocket(path="/?token=s3cret")
    ok.feed(json.dumps({"type": "future-extension"}))
    ok.end()
    await asyncio.wait_for(server.handle_signaling(ok), timeout=5)
    assert ok.close_code != 4401, "valid token was rejected"


@pytest.mark.asyncio
async def test_origin_allowlist_rejects_unknown_origin():
    config = make_config(QVS_ALLOWED_ORIGINS="https://app.example.com")
    assert getattr(config, "allowed_origins", None) == ["https://app.example.com"]
    server = make_server(config)

    bad = FakeWebSocket(request_headers={"Origin": "https://evil.example.com"})
    bad.end()
    await asyncio.wait_for(server.handle_signaling(bad), timeout=5)
    assert bad.closed and bad.close_code == 4403, "disallowed Origin was accepted"

    good = FakeWebSocket(request_headers={"Origin": "https://app.example.com"})
    good.feed(json.dumps({"type": "future-extension"}))
    good.end()
    await asyncio.wait_for(server.handle_signaling(good), timeout=5)
    assert good.close_code != 4403, "allowlisted Origin was rejected"


@pytest.mark.asyncio
async def test_connection_cap_evicts_oldest_session():
    """The server targets one headset (shared detector state): a new
    connection at the cap must supersede the old one, not stack onto it."""
    config = make_config()
    assert getattr(config, "max_connections", None) == 1, "config lacks max_connections"
    server = make_server(config)

    ws1 = FakeWebSocket()
    task1 = asyncio.create_task(server.handle_signaling(ws1))
    await asyncio.sleep(0.05)
    assert len(server.pcs) == 1

    ws2 = FakeWebSocket()
    task2 = asyncio.create_task(server.handle_signaling(ws2))
    await asyncio.sleep(0.1)

    assert ws1.closed and ws1.close_code == 4000, "old session was not evicted"
    await asyncio.wait_for(task1, timeout=5)
    assert len(server.pcs) == 1, f"expected 1 live pc after eviction, got {len(server.pcs)}"

    ws2.end()
    await asyncio.wait_for(task2, timeout=5)
    assert len(server.pcs) == 0


@pytest.mark.asyncio
async def test_all_track_tasks_are_cancelled_on_teardown(monkeypatch):
    """S5: if more than one video track is negotiated, every processing task
    must be cancelled at teardown — not just the most recent one."""
    import questvisionstream.webrtc_server as ws_mod

    captured: list = []
    real_pc = ws_mod.RTCPeerConnection

    class CapturingPC(real_pc):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, **kwargs)
            captured.append(self)

    monkeypatch.setattr(ws_mod, "RTCPeerConnection", CapturingPC)

    config = make_config()
    server = make_server(config)
    ws = FakeWebSocket()
    task = asyncio.create_task(server.handle_signaling(ws))
    await asyncio.sleep(0.05)
    assert captured, "peer connection was not created"
    pc = captured[0]

    track_a, track_b = HangingTrack(), HangingTrack()
    pc.emit("track", track_a)
    await asyncio.sleep(0.05)
    pc.emit("track", track_b)
    await asyncio.sleep(0.05)

    ws.end()
    await asyncio.wait_for(task, timeout=5)
    # Give cancelled tasks a beat to observe the CancelledError.
    await asyncio.sleep(0.05)

    leaked = [name for name, t in (("first", track_a), ("second", track_b)) if not t.cancelled]
    # Clean up anything the server leaked so it doesn't outlive the test.
    for t in asyncio.all_tasks():
        if t is not asyncio.current_task() and not t.done():
            t.cancel()
    assert not leaked, f"{leaked} track task(s) leaked at teardown"
