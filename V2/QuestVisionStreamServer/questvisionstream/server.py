"""QuestVisionStream server entry point."""
from __future__ import annotations

import argparse
import asyncio
from dataclasses import replace

from .config import ServerConfig, load_config
from .detectors import DETECTOR_NAMES, get_detector
from .health import start_health_server
from .video_processor import VideoProcessor
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

    print(f"QuestVisionStream Server | detector={config.detector} | display={config.enable_display}")

    # Load the model once and share it across connections.
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
