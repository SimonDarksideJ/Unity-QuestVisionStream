"""The aioice STUN-retry-after-close noise filter suppresses only that signature."""
from __future__ import annotations

import asyncio

import pytest

from questvisionstream.loop_noise import (
    _is_stun_retry_teardown_noise,
    install_stun_retry_filter,
)


class _FakeHandle:
    """Repr matches asyncio's TimerHandle for the aioice retry callback."""

    def __repr__(self) -> str:
        return "<TimerHandle when=1284414.59 Transaction.__retry()>"


def _stun_retry_context() -> dict:
    # Exactly what asyncio hands the loop handler when Transaction.__retry()
    # raises AttributeError after the transport/loop are already None.
    return {
        "message": "Exception in callback Transaction.__retry()",
        "exception": AttributeError("'NoneType' object has no attribute 'sendto'"),
        "handle": _FakeHandle(),
    }


def test_detects_stun_retry_noise():
    assert _is_stun_retry_teardown_noise(_stun_retry_context()) is True


def test_does_not_match_real_errors():
    # A genuine error elsewhere must NOT be classified as noise.
    assert not _is_stun_retry_teardown_noise(
        {"message": "Task exception was never retrieved", "exception": ValueError("boom")}
    )
    # Same callback but a non-AttributeError is not the teardown signature.
    assert not _is_stun_retry_teardown_noise(
        {"message": "Exception in callback Transaction.__retry()", "exception": RuntimeError("x")}
    )


@pytest.mark.asyncio
async def test_filter_swallows_noise_and_passes_real_errors():
    loop = asyncio.get_running_loop()
    seen: list[dict] = []
    loop.set_exception_handler(lambda _loop, ctx: seen.append(ctx))  # prior handler

    flt = install_stun_retry_filter(loop)

    # Noise: swallowed, counted, not delegated to the prior handler.
    loop.call_exception_handler(_stun_retry_context())
    assert flt.suppressed == 1
    assert seen == []

    # Real error: delegated through to the prior handler untouched.
    real = {"message": "Task exception", "exception": ValueError("boom")}
    loop.call_exception_handler(real)
    assert flt.suppressed == 1
    assert seen == [real]


@pytest.mark.asyncio
async def test_matches_via_handle_repr_when_message_absent():
    loop = asyncio.get_running_loop()
    flt = install_stun_retry_filter(loop)
    loop.call_exception_handler(
        {"message": "", "exception": AttributeError("no sendto"), "handle": _FakeHandle()}
    )
    assert flt.suppressed == 1
