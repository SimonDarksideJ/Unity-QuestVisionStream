"""WebRTC signaling + peer connection server (aiortc + websockets).

Configurable ICE/STUN/TURN from the environment. The client is the offerer and
creates the `detections` data channel; this server listens for it and pushes
detection payloads over it.

ICE candidate note: aiortc's ``candidate_to_sdp`` emits the candidate line
WITHOUT the ``candidate:`` prefix, and ``candidate_from_sdp`` expects it that way.
The browser/WebXR client is responsible for stripping/adding that prefix (see the
`com.questvisionstream` WebRTCService); this server stays in aiortc's native form.
"""
from __future__ import annotations

import asyncio
import json
from typing import Optional

import websockets
from aiortc import (
    RTCConfiguration,
    RTCDataChannel,
    RTCIceServer,
    RTCPeerConnection,
    RTCSessionDescription,
)
from aiortc.sdp import candidate_from_sdp, candidate_to_sdp

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


class WebRTCServer:
    def __init__(self, config: ServerConfig, make_processor) -> None:
        self.config = config
        # Factory so each connection gets a fresh VideoProcessor + detector state.
        self.make_processor = make_processor
        self.pcs: set[RTCPeerConnection] = set()

    async def handle_signaling(self, websocket) -> None:
        print("[WebRTC] Client connected")
        pc = RTCPeerConnection(RTCConfiguration(iceServers=_build_ice_servers(self.config)))
        self.pcs.add(pc)

        processor: VideoProcessor = self.make_processor()
        detections_channel: Optional[RTCDataChannel] = None
        video_task: Optional[asyncio.Task] = None
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
            nonlocal video_task
            print(f"[WebRTC] Track: {track.kind}")
            if track.kind == "video":
                video_task = asyncio.create_task(processor.process_video_stream(track))

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
                    except Exception:
                        pass

        @pc.on("icecandidate")
        async def _on_icecandidate(candidate) -> None:
            if candidate is None:
                return
            try:
                await websocket.send(
                    json.dumps(
                        {
                            "type": "candidate",
                            "candidate": candidate_to_sdp(candidate),
                            "sdpMid": candidate.sdpMid,
                            "sdpMLineIndex": candidate.sdpMLineIndex,
                        }
                    )
                )
            except Exception:
                pass

        try:
            async for message in websocket:
                data = json.loads(message)
                if data["type"] == "offer":
                    if offer_received:
                        continue
                    offer_received = True
                    await pc.setRemoteDescription(RTCSessionDescription(sdp=data["sdp"], type="offer"))
                    answer = await pc.createAnswer()
                    await pc.setLocalDescription(answer)
                    await websocket.send(json.dumps({"type": "answer", "sdp": pc.localDescription.sdp}))
                    print("[WebRTC] Answer sent")
                elif data["type"] == "candidate":
                    cand = candidate_from_sdp(data["candidate"])
                    cand.sdpMid = data["sdpMid"]
                    cand.sdpMLineIndex = int(data["sdpMLineIndex"])
                    await pc.addIceCandidate(cand)
        except websockets.ConnectionClosed:
            print("[WebRTC] Client disconnected")
        except Exception as exc:
            print(f"[WebRTC] Error: {exc}")
        finally:
            if video_task and not video_task.done():
                video_task.cancel()
                try:
                    await video_task
                except asyncio.CancelledError:
                    pass
            await pc.close()
            self.pcs.discard(pc)

    async def start(self) -> None:
        print(f"[WebRTC] Signaling on ws://{self.config.host}:{self.config.port}")
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
