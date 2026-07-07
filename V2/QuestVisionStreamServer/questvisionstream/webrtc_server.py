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
import os
import re
from datetime import datetime
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
from .detection_log import create_detection_log
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


def _client_cid(websocket) -> str:
    """The client's per-page connection id (``?cid=`` on the WS URL), if any.

    Lets the server tell a **reconnect of the same page** (clean replace) from a
    **genuinely different client** (connection cap). Empty when absent."""
    query = parse_qs(urlparse(_request_path(websocket)).query)
    return query.get("cid", [""])[0]


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
        # Oldest-first (websocket, pc, cid) tuples for reconnect-replace + cap.
        self.sessions: list[tuple[object, RTCPeerConnection, str]] = []
        # Capture layout. When a base capture dir is configured, each connection
        # gets its OWN folder (captures + detection log together) — so nothing
        # collides across sessions; the shared single-file log is the fallback
        # used only when no capture dir is set.
        self._capture_base = config.capture_dir.strip()
        self.detection_log = (
            None if self._capture_base else create_detection_log(config.detection_log)
        )

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

    async def _close_session(self, old_ws, old_pc, code: int, reason: str) -> None:
        try:
            await old_ws.close(code, reason)
        except Exception as exc:
            print(f"[WebRTC] Error closing socket: {exc}")
        try:
            await old_pc.close()
        except Exception as exc:
            print(f"[WebRTC] Error closing pc: {exc}")

    async def _admit(self, cid: str) -> None:
        """Decide what to close before admitting a new connection.

        1. **Reconnect of the same page** (matching ``cid``): retire its prior,
           possibly half-open session — a clean *replace*, not a supersede war.
           (This is the fix for a socket that flaps through a proxy: the new
           connection cleanly takes over instead of the server evicting a live
           session and the client reconnecting forever.)
        2. **Genuinely different clients** still over the cap: supersede the
           oldest (close 4000), as before.
        """
        if cid:
            stale = [s for s in self.sessions if s[2] == cid]
            for old in stale:
                self.sessions.remove(old)
                print(f"[QVS] Reconnect — replacing prior session for client {cid[:8]}")
                await self._close_session(old[0], old[1], 4001, "replaced by reconnect")
        while len(self.sessions) >= self.config.max_connections:
            old_ws, old_pc, _ = self.sessions.pop(0)
            print("[WebRTC] Connection cap reached — superseding oldest session")
            await self._close_session(old_ws, old_pc, 4000, "superseded by a newer connection")

    def _session_dir(self, client: str) -> str:
        """Path of this connection's capture folder: ``<base>/<start>_<client>``.

        Named by connection start time + client so sessions never collide.
        Returns the path only — the folder is created lazily on the first
        capture/log write (so connections that never stream leave no empty
        folders)."""
        stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
        safe = re.sub(r"[^A-Za-z0-9._-]", "_", client).strip("_") or "client"
        return os.path.join(self._capture_base, f"{stamp}_{safe}")

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
        cid = _client_cid(websocket)
        await self._admit(cid)

        client = _client_ident(websocket)
        print(f"[QVS] Client connected: {client}")
        pc = RTCPeerConnection(RTCConfiguration(iceServers=_build_ice_servers(self.config)))
        self.pcs.add(pc)
        session = (websocket, pc, cid)
        self.sessions.append(session)

        processor: VideoProcessor = self.make_processor()

        # Per-connection capture folder (created lazily on first write): give THIS
        # connection its own dated, client-named subfolder holding both its frame
        # dumps and its detection log, so sessions never collide. Falls back to
        # the shared single-file log when no capture dir is configured.
        connection_log = None
        if self._capture_base:
            session_dir = self._session_dir(client)
            processor.set_capture_dir(session_dir)
            connection_log = create_detection_log(os.path.join(session_dir, "detections.jsonl"))
            print(f"[QVS] Session capture → {session_dir}")
        detection_log = connection_log if connection_log is not None else self.detection_log

        detections_channel: Optional[RTCDataChannel] = None
        video_tasks: list[asyncio.Task] = []
        offer_received = False

        def send_over_dc(payload: dict) -> None:
            if detections_channel and detections_channel.readyState == "open":
                try:
                    detections_channel.send(json.dumps(payload))
                except Exception as exc:
                    print(f"[WebRTC] DC send failed: {exc}")
                    return
                # Record exactly what the client received (logged only on a
                # successful send, so the file mirrors the wire, not intent).
                if detection_log is not None and payload.get("type") == "detections":
                    detection_log.log(client, payload)

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
                    elif data["type"] == "status":
                        # Client → server device/telemetry status (camera, signaling,
                        # errors). Surfaced so device-side problems are visible here.
                        # `__`-prefixed fields (e.g. __keepalive) are traffic-only —
                        # they keep proxies from idling the socket; don't log them.
                        field = str(data.get("field", "?"))
                        if not field.startswith("__"):
                            value = data.get("value", "")
                            print(f"[QVS ◂] {client} status: {field} = {value}")
                    # Unknown types are ignored (forward compatibility).
                except Exception as exc:
                    print(f"[WebRTC] Error handling '{data['type']}' message: {exc}")
        except websockets.ConnectionClosed:
            # Log what the SERVER observed, so we can tell who closed it:
            #  - close_code 1011        → the server's own ping-timeout closed it
            #  - 1000/1001 (clean)      → the client/proxy closed deliberately
            #  - 1006 / None (abrupt)   → the transport dropped (no close frame)
            code = getattr(websocket, "close_code", None)
            reason = getattr(websocket, "close_reason", None)
            print(f"[QVS] Client disconnected: {client} (server saw close_code={code} reason={reason!r})")
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
            if connection_log is not None:
                connection_log.close()

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
        if self.detection_log is not None:
            self.detection_log.close()
