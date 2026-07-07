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


def _env_int(name: str, default: int, minimum: int | None = None) -> int:
    val = os.getenv(name)
    try:
        result = int(val) if val is not None else default
    except ValueError:
        result = default
    if minimum is not None and result < minimum:
        return minimum
    return result


def _env_str(name: str, default: str) -> str:
    val = os.getenv(name)
    return val if val is not None and val != "" else default


def _env_list(name: str) -> list[str]:
    """Comma-separated env var → list of trimmed non-empty entries."""
    return [item.strip() for item in os.getenv(name, "").split(",") if item.strip()]


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

    # Display / debug. Headless by default. The log interval is a modulo
    # divisor in the frame loop, so it is clamped to >= 1.
    enable_display: bool = field(default_factory=lambda: _env_bool("QVS_ENABLE_DISPLAY", False))
    log_interval: int = field(default_factory=lambda: _env_int("QVS_LOG_INTERVAL", 30, minimum=1))

    # Separate machine-readable detection log. When set to a path, every payload
    # sent to a client is appended as one JSON line (see detection_log.py) for
    # frame-for-frame comparison against a client-side capture. Empty = disabled;
    # the launch scripts default it next to server.log.
    detection_log: str = field(default_factory=lambda: _env_str("QVS_DETECTION_LOG", ""))

    # libav/libswscale (PyAV) console verbosity. Default "error" silences the
    # benign, per-frame "[swscaler] No accelerated colorspace conversion from
    # yuv420p to bgr24" WARNING that otherwise floods the log; set "warning" or
    # higher to bring ffmpeg diagnostics back.
    ffmpeg_log_level: str = field(default_factory=lambda: _env_str("QVS_FFMPEG_LOG_LEVEL", "error"))

    # Session security. All default open for trusted-LAN use; set them when the
    # server is reachable beyond the LAN (tunnel, port-forward, public host).
    #
    # QVS_AUTH_TOKEN: when set, clients must dial ws(s)://host:port/?token=<value>.
    # QVS_ALLOWED_ORIGINS: comma-separated Origin allowlist (empty = allow all).
    # QVS_MAX_CONNECTIONS: concurrent session cap; a new connection at the cap
    #   supersedes the oldest one (the shared detector targets one headset).
    auth_token: str = field(default_factory=lambda: _env_str("QVS_AUTH_TOKEN", ""))
    allowed_origins: list[str] = field(default_factory=lambda: _env_list("QVS_ALLOWED_ORIGINS"))
    max_connections: int = field(default_factory=lambda: _env_int("QVS_MAX_CONNECTIONS", 1, minimum=1))

    # Frame pre-processing.
    flip_vertical: bool = field(default_factory=lambda: _env_bool("QVS_FLIP_VERTICAL", True))
    flip_horizontal: bool = field(default_factory=lambda: _env_bool("QVS_FLIP_HORIZONTAL", False))
    rotate_180: bool = field(default_factory=lambda: _env_bool("QVS_ROTATE_180", False))

    ice: IceConfig = field(default_factory=IceConfig)


def load_config() -> ServerConfig:
    """Build the configuration from the current environment."""
    return ServerConfig()
