"""Config parsing/clamping, detector registry, device-dependent flags, and
health-endpoint hardening.
"""
from __future__ import annotations

import asyncio
import json

import pytest

from questvisionstream.detectors import DETECTOR_NAMES, get_detector
from questvisionstream.detectors.base import clamp_box

from conftest import make_config


# ---------------------------------------------------------------- config ----

def test_log_interval_is_clamped_to_positive():
    """S4 at the source: QVS_LOG_INTERVAL=0 (or negative) must never reach the
    frame loop as a modulo divisor."""
    assert make_config(QVS_LOG_INTERVAL="0").log_interval >= 1
    assert make_config(QVS_LOG_INTERVAL="-5").log_interval >= 1
    assert make_config(QVS_LOG_INTERVAL="30").log_interval == 30


def test_max_connections_defaults_to_single_headset_and_clamps():
    assert make_config().max_connections == 1
    assert make_config(QVS_MAX_CONNECTIONS="0").max_connections >= 1
    assert make_config(QVS_MAX_CONNECTIONS="4").max_connections == 4


def test_security_settings_default_open_for_lan():
    config = make_config()
    assert config.auth_token == ""
    assert config.allowed_origins == []
    multi = make_config(QVS_ALLOWED_ORIGINS="https://a.example, https://b.example")
    assert multi.allowed_origins == ["https://a.example", "https://b.example"]


def test_env_int_garbage_falls_back_to_default():
    assert make_config(QVS_PORT="not-a-number").port == 3000


# -------------------------------------------------------------- registry ----

def test_registry_names_are_stable_contract():
    assert DETECTOR_NAMES == ["yolo", "florence2", "owlv2", "grounding_dino", "body"]


def test_unknown_detector_raises_with_available_names():
    with pytest.raises(ValueError) as excinfo:
        get_detector("owl2")  # the historical V1 typo
    assert "owlv2" in str(excinfo.value)


# ------------------------------------------------------------- detectors ----

def test_clamp_box_bounds_and_rounding():
    assert clamp_box(-10, -10, 700, 500, 640, 480) == (0, 0, 639, 479)
    assert clamp_box(10.4, 10.6, 20.4, 20.6, 640, 480) == (10, 11, 20, 21)


def test_half_precision_only_on_cuda():
    """FP16 inference is CUDA-only; requesting it on cpu/mps must be refused."""
    from questvisionstream.detectors.base import resolve_half

    assert resolve_half(True, "cuda") is True
    assert resolve_half(True, "cpu") is False
    assert resolve_half(True, "mps") is False
    assert resolve_half(False, "cuda") is False


# ---------------------------------------------------------------- health ----

@pytest.mark.asyncio
async def test_health_endpoint_serves_status_and_times_out_slowloris(monkeypatch):
    """The health server must answer a normal probe and must not hold a
    connection open forever for a client that never finishes its headers."""
    import questvisionstream.health as health_mod

    monkeypatch.setattr(health_mod, "HEADER_TIMEOUT_S", 0.3, raising=True)

    config = make_config(QVS_HOST="127.0.0.1", QVS_HEALTH_PORT="0")
    server = await health_mod.start_health_server(config, lambda: {"status": "ok"})
    port = server.sockets[0].getsockname()[1]
    try:
        # Normal probe.
        reader, writer = await asyncio.open_connection("127.0.0.1", port)
        writer.write(b"GET / HTTP/1.1\r\nHost: x\r\n\r\n")
        await writer.drain()
        response = await asyncio.wait_for(reader.read(), timeout=2)
        assert b"200 OK" in response
        assert json.loads(response.split(b"\r\n\r\n", 1)[1]) == {"status": "ok"}
        writer.close()

        # Slowloris: request line, one header, then silence (no blank line).
        reader2, writer2 = await asyncio.open_connection("127.0.0.1", port)
        writer2.write(b"GET / HTTP/1.1\r\nHost: x\r\n")
        await writer2.drain()
        # The server must close the connection on its own within the timeout.
        data = await asyncio.wait_for(reader2.read(), timeout=2)
        assert data == b"" or b"HTTP/1.1" in data, "connection held open indefinitely"
        writer2.close()
    finally:
        server.close()
        await server.wait_closed()
