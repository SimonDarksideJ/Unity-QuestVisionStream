"""WebRTC signaling + peer connection server (aiortc + websockets).

Configurable ICE/STUN/TURN from the environment. The client is the offerer and
creates the `detections` data channel; this server listens for it and pushes
detection payloads over it.

ICE candidate notes:
- aiortc's ``candidate_from_sdp`` expects the candidate line WITHOUT the
  ``candidate:`` prefix; the browser/WebXR client strips it on send (see the
  `com.questvisionstream` WebRTCService).
- aiortc does NOT trickle its own candidates: it gathers ICE fully before
  ``createAnswer`` resolves, so every server candidate is already embedded in
  the answer SDP. There is deliberately no server→client ``candidate`` message.

Session policy: signaling messages are validated per-message (a malformed
message is logged and skipped, never tears the session down), and connections
are gated by optional auth token / Origin allowlist plus a connection cap that
supersedes the oldest session (the shared detector targets one headset).
"""
from __future__ import annotations

import asyncio
import json
from typing import Optional
from urllib.parse import parse_qs, urlparse

import websockets
from aiortc import (
    RTCConfiguration,
    RTCDataChannel,
    RTCIceServer,
    RTCPeerConnection,
    RTCSessionDescription,
)
from aiortc.sdp import candidate_from_sdp

from .config import ServerConfig
from .video_processor import VideoProcessor


def _build_ice_servers(config: ServerConfig) -> list[RTCIceServer]:
    servers: list[RTCIceServer] = []
    stun_urls = [u for u in config.ice.stun_urls if u]
    if stun_urls:
        servers.append(RTCIceServer(urls=stun_urls))
    if config.ice.enable_turn and config.ice.turn_urls:
        servers.append(
            RTCIceServer(
                urls=config.ice.turn_urls,
                username=config.ice.turn_username or None,
                credential=config.ice.turn_credential or None,
            )
        )
    return servers


def _request_path(websocket) -> str:
    """The request path across websockets versions (``.path`` pre-14,
    ``.request.path`` after)."""
    path = getattr(websocket, "path", None)
    if path is None:
        path = getattr(getattr(websocket, "request", None), "path", "/")
    return path or "/"


def _request_origin(websocket) -> Optional[str]:
    headers = getattr(websocket, "request_headers", None)
    if headers is None:
        headers = getattr(getattr(websocket, "request", None), "headers", {})
    getter = getattr(headers, "get", None)
    return getter("Origin") if getter else None


def _client_ident(websocket) -> str:
    """A human-readable identity for the connecting client.

    Behind ``tailscale serve`` (or any reverse proxy) the socket peer is the
    local proxy, so prefer the forwarded identity/address it injects — Tailscale
    adds ``Tailscale-User-Login`` and ``X-Forwarded-For`` — and fall back to the
    raw socket peer for direct LAN connections.
    """
    headers = getattr(websocket, "request_headers", None)
    if headers is None:
        headers = getattr(getattr(websocket, "request", None), "headers", {})
    getter = getattr(headers, "get", None)
    if getter:
        for name in ("Tailscale-User-Login", "X-Forwarded-For"):
            value = getter(name)
            if value:
                return str(value).split(",")[0].strip()
    addr = getattr(websocket, "remote_address", None)
    if isinstance(addr, (tuple, list)) and len(addr) >= 2:
        return f"{addr[0]}:{addr[1]}"
    return "unknown"


def _parse_signaling_message(raw) -> Optional[dict]:
    """Decode one signaling message; None if it isn't a typed JSON object."""
    try:
        data = json.loads(raw)
    except (TypeError, ValueError):
        return None
    if not isinstance(data, dict) or not isinstance(data.get("type"), str):
        return None
    return data


class WebRTCServer:
    def __init__(self, config: ServerConfig, make_processor) -> None:
        self.config = config
        # Fresh VideoProcessor per connection. NOTE: the detector inside it is
        # SHARED across connections (one model load); stateful detectors
        # (florence2 frame-skip cache, body tracking) assume a single active
        # stream — which the connection cap enforces by default.
        self.make_processor = make_processor
        self.pcs: set[RTCPeerConnection] = set()
        # Oldest-first (websocket, pc) pairs for cap enforcement/eviction.
        self.sessions: list[tuple[object, RTCPeerConnection]] = []

    # ------------------------------------------------------------ gating ----

    def _authorized(self, websocket) -> bool:
        if not self.config.auth_token:
            return True
        query = parse_qs(urlparse(_request_path(websocket)).query)
        supplied = query.get("token", [""])[0]
        return supplied == self.config.auth_token

    def _origin_allowed(self, websocket) -> bool:
        if not self.config.allowed_origins:
            return True
        origin = _request_origin(websocket)
        return origin in self.config.allowed_origins

    async def _enforce_connection_cap(self) -> None:
        """A new connection at the cap supersedes the oldest live session —
        right for a single-headset server where the old socket may linger
        half-open (e.g. headset slept mid-session)."""
        while len(self.sessions) >= self.config.max_connections:
            old_ws, old_pc = self.sessions.pop(0)
            print("[WebRTC] Connection cap reached — superseding oldest session")
            try:
                await old_ws.close(4000, "superseded by a newer connection")
            except Exception as exc:
                print(f"[WebRTC] Error closing superseded socket: {exc}")
            try:
                await old_pc.close()
            except Exception as exc:
                print(f"[WebRTC] Error closing superseded pc: {exc}")

    # ----------------------------------------------------------- session ----

    async def handle_signaling(self, websocket) -> None:
        if not self._authorized(websocket):
            print("[WebRTC] Rejected connection: missing/invalid auth token")
            await websocket.close(4401, "unauthorized")
            return
        if not self._origin_allowed(websocket):
            print(f"[WebRTC] Rejected connection: origin {_request_origin(websocket)!r} not allowed")
            await websocket.close(4403, "origin not allowed")
            return
        await self._enforce_connection_cap()

        client = _client_ident(websocket)
        print(f"[QVS] Client connected: {client}")
        pc = RTCPeerConnection(RTCConfiguration(iceServers=_build_ice_servers(self.config)))
        self.pcs.add(pc)
        session = (websocket, pc)
        self.sessions.append(session)

        processor: VideoProcessor = self.make_processor()
        detections_channel: Optional[RTCDataChannel] = None
        video_tasks: list[asyncio.Task] = []
        offer_received = False

        def send_over_dc(payload: dict) -> None:
            if detections_channel and detections_channel.readyState == "open":
                try:
                    detections_channel.send(json.dumps(payload))
                except Exception as exc:
                    print(f"[WebRTC] DC send failed: {exc}")

        processor.send = send_over_dc

        @pc.on("iceconnectionstatechange")
        async def _on_ice() -> None:
            print(f"[WebRTC] ICE: {pc.iceConnectionState}")

        @pc.on("connectionstatechange")
        async def _on_conn() -> None:
            print(f"[WebRTC] PC: {pc.connectionState}")

        @pc.on("track")
        async def _on_track(track) -> None:
            print(f"[WebRTC] Track: {track.kind}")
            if track.kind == "video":
                video_tasks.append(asyncio.create_task(processor.process_video_stream(track)))

        @pc.on("datachannel")
        def _on_datachannel(channel: RTCDataChannel) -> None:
            nonlocal detections_channel
            print(f"[WebRTC] DataChannel: {channel.label}")
            if channel.label == "detections":
                detections_channel = channel

                @channel.on("open")
                def _on_open() -> None:
                    try:
                        channel.send(json.dumps({"type": "ready"}))
                    except Exception as exc:
                        print(f"[WebRTC] Ready message failed: {exc}")

        async def on_offer(data: dict) -> None:
            nonlocal offer_received
            if offer_received:
                return
            sdp = data.get("sdp")
            if not isinstance(sdp, str):
                print("[WebRTC] Ignoring offer without sdp")
                return
            offer_received = True
            await pc.setRemoteDescription(RTCSessionDescription(sdp=sdp, type="offer"))
            answer = await pc.createAnswer()
            await pc.setLocalDescription(answer)
            await websocket.send(json.dumps({"type": "answer", "sdp": pc.localDescription.sdp}))
            print("[WebRTC] Answer sent")

        async def on_candidate(data: dict) -> None:
            candidate = data.get("candidate")
            if not isinstance(candidate, str) or "sdpMid" not in data:
                print("[WebRTC] Ignoring malformed candidate message")
                return
            cand = candidate_from_sdp(candidate)
            cand.sdpMid = data["sdpMid"]
            cand.sdpMLineIndex = int(data.get("sdpMLineIndex") or 0)
            await pc.addIceCandidate(cand)

        try:
            async for message in websocket:
                data = _parse_signaling_message(message)
                if data is None:
                    print("[WebRTC] Ignoring malformed signaling message")
                    continue
                # A bad message is logged and skipped; it must not end the session.
                try:
                    if data["type"] == "offer":
                        await on_offer(data)
                    elif data["type"] == "candidate":
                        await on_candidate(data)
                    # Unknown types are ignored (forward compatibility).
                except Exception as exc:
                    print(f"[WebRTC] Error handling '{data['type']}' message: {exc}")
        except websockets.ConnectionClosed:
            print(f"[QVS] Client disconnected: {client}")
        except Exception as exc:
            print(f"[WebRTC] Session error: {exc}")
        finally:
            for task in video_tasks:
                if not task.done():
                    task.cancel()
            for task in video_tasks:
                try:
                    await task
                except asyncio.CancelledError:
                    pass
                except Exception:
                    pass
            await pc.close()
            self.pcs.discard(pc)
            if session in self.sessions:
                self.sessions.remove(session)

    async def start(self) -> None:
        print(f"[WebRTC] Signaling on ws://{self.config.host}:{self.config.port}")
        print(
            f"[WebRTC] auth={'on' if self.config.auth_token else 'off'} | "
            f"origins={self.config.allowed_origins or 'any'} | "
            f"max_connections={self.config.max_connections}"
        )
        async with websockets.serve(
            self.handle_signaling,
            self.config.host,
            self.config.port,
            ping_interval=20,
            ping_timeout=20,
        ):
            await asyncio.Future()

    async def close(self) -> None:
        await asyncio.gather(*(pc.close() for pc in self.pcs), return_exceptions=True)
        self.pcs.clear()
        self.sessions.clear()
