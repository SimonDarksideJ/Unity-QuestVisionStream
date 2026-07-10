"""QuestVisionStream server entry point."""
from __future__ import annotations

import argparse
import asyncio
from dataclasses import replace

from .config import ServerConfig, load_config
from .detectors import DETECTOR_NAMES, get_detector
from .health import start_health_server
from .loop_noise import install_stun_retry_filter
from .video_processor import VideoProcessor, configure_ffmpeg_logging
from .webrtc_server import WebRTCServer


def parse_args(config: ServerConfig) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="QuestVisionStream Server")
    # Choices come from the registry so they can never drift from the implementation.
    parser.add_argument("--detector", choices=DETECTOR_NAMES, default=config.detector,
                        help=f"Detector to use (default: {config.detector})")
    parser.add_argument("--host", default=config.host)
    parser.add_argument("--port", type=int, default=config.port)
    return parser.parse_args()


async def run() -> None:
    config = load_config()
    args = parse_args(config)
    config = replace(config, detector=args.detector, host=args.host, port=args.port)

    # Drop aioice's benign post-teardown STUN-retry tracebacks (see loop_noise);
    # they otherwise flood the console every time a peer connection closes.
    install_stun_retry_filter(asyncio.get_running_loop())

    print(f"QuestVisionStream Server | detector={config.detector} | display={config.enable_display}")

    # Quiet libswscale's benign per-frame "no accelerated colorspace conversion"
    # WARNING before any frame is decoded (see configure_ffmpeg_logging).
    configure_ffmpeg_logging(config.ffmpeg_log_level)

    # Import cv2 now, on the main thread during boot. It is otherwise imported
    # lazily inside _preprocess on the inference worker thread on the FIRST
    # frame — a ~100–160ms, GIL-holding import that stalls the event loop
    # (signaling/keepalives/other peers) mid-session. Best-effort: the lazy
    # import still covers environments where cv2 is unavailable.
    try:
        import cv2  # noqa: F401
    except Exception as exc:  # pragma: no cover - environment-dependent
        print(f"[Server] cv2 pre-import skipped ({exc}); will import lazily")

    # Load the model once and share it across connections. Stateful detectors
    # (florence2, body) assume a single active stream — QVS_MAX_CONNECTIONS
    # defaults to 1 and the newest connection supersedes the oldest, so shared
    # state is never fed by two streams at once. The single inference worker
    # thread (video_processor) additionally serializes access to the model.
    detector = get_detector(config.detector)

    def noop_send(_: dict) -> None:
        return None

    def make_processor() -> VideoProcessor:
        # send is rebound per-connection by the WebRTC server to its data channel.
        return VideoProcessor(config, detector.detect, noop_send)

    server = WebRTCServer(config, make_processor)

    def status() -> dict:
        return {"status": "ok", "detector": config.detector, "connections": len(server.pcs)}

    health = await start_health_server(config, status)
    try:
        await server.start()
    finally:
        health.close()
        await server.close()
        detector.close()


def main() -> None:
    try:
        asyncio.run(run())
    except KeyboardInterrupt:
        print("\nShutting down...")


if __name__ == "__main__":
    main()
