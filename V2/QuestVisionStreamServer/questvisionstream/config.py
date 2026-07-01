"""Environment-driven configuration for QuestVisionStreamServer.

Every setting is overridable via a ``QVS_*`` environment variable so the server
is portable across native macOS/MPS, Docker (CPU), and Hugging Face Spaces
without code edits. Display is OFF by default (headless-safe).
"""
from __future__ import annotations

import os
from dataclasses import dataclass, field


def _env_bool(name: str, default: bool) -> bool:
    val = os.getenv(name)
    if val is None:
        return default
    return val.strip().lower() in {"1", "true", "yes", "on"}


def _env_int(name: str, default: int) -> int:
    val = os.getenv(name)
    try:
        return int(val) if val is not None else default
    except ValueError:
        return default


def _env_str(name: str, default: str) -> str:
    val = os.getenv(name)
    return val if val is not None and val != "" else default


@dataclass(frozen=True)
class IceConfig:
    """STUN/TURN configuration for the peer connection."""

    stun_urls: list[str] = field(
        default_factory=lambda: _env_str("QVS_STUN_URLS", "stun:stun.l.google.com:19302").split(",")
    )
    enable_turn: bool = field(default_factory=lambda: _env_bool("QVS_ENABLE_TURN", False))
    turn_urls: list[str] = field(
        default_factory=lambda: [
            u for u in _env_str("QVS_TURN_URLS", "").split(",") if u
        ]
    )
    turn_username: str = field(default_factory=lambda: _env_str("QVS_TURN_USERNAME", ""))
    turn_credential: str = field(default_factory=lambda: _env_str("QVS_TURN_CREDENTIAL", ""))


@dataclass(frozen=True)
class ServerConfig:
    """Top-level server configuration."""

    host: str = field(default_factory=lambda: _env_str("QVS_HOST", "0.0.0.0"))
    port: int = field(default_factory=lambda: _env_int("QVS_PORT", 3000))
    health_port: int = field(default_factory=lambda: _env_int("QVS_HEALTH_PORT", 8080))

    detector: str = field(default_factory=lambda: _env_str("QVS_DETECTOR", "yolo"))

    # Display / debug. Headless by default.
    enable_display: bool = field(default_factory=lambda: _env_bool("QVS_ENABLE_DISPLAY", False))
    log_interval: int = field(default_factory=lambda: _env_int("QVS_LOG_INTERVAL", 30))

    # Frame pre-processing.
    flip_vertical: bool = field(default_factory=lambda: _env_bool("QVS_FLIP_VERTICAL", True))
    flip_horizontal: bool = field(default_factory=lambda: _env_bool("QVS_FLIP_HORIZONTAL", False))
    rotate_180: bool = field(default_factory=lambda: _env_bool("QVS_ROTATE_180", False))

    ice: IceConfig = field(default_factory=IceConfig)


def load_config() -> ServerConfig:
    """Build the configuration from the current environment."""
    return ServerConfig()
