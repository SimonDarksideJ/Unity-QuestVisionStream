"""Silence a specific, benign asyncio flood from aioice (aiortc's ICE layer).

When a peer connection is torn down, aioice may still have scheduled STUN
retransmission timers (``Transaction.__retry``). When one fires after the
transport is gone, ``transport.sendto`` hits a ``None`` socket, and asyncio's
own error path then hits a ``None`` loop — producing a multi-frame traceback
per timer:

    Exception in callback Transaction.__retry()
    ...
    AttributeError: 'NoneType' object has no attribute 'sendto'
    ...
    AttributeError: 'NoneType' object has no attribute 'call_exception_handler'

Each closed connection emits a bounded burst of these (STUN caps its
retransmits), so it is console noise on teardown — NOT a leak or a failed
session (real media has already flowed by the time the connection closes). This
installs a loop exception handler that drops exactly that signature and delegates
everything else to the previous/default handler, so genuine errors still surface.

A single one-line notice is printed the first time it fires, so the suppression
is discoverable rather than silent.
"""
from __future__ import annotations

import asyncio


class _StunRetryFilter:
    def __init__(self, loop: asyncio.AbstractEventLoop) -> None:
        self._previous = loop.get_exception_handler()
        self.suppressed = 0

    def __call__(self, loop: asyncio.AbstractEventLoop, context: dict) -> None:
        if _is_stun_retry_teardown_noise(context):
            self.suppressed += 1
            if self.suppressed == 1:
                print(
                    "[QVS] Suppressing benign aioice STUN-retry-after-close noise "
                    "(aiortc teardown artifact; connections/media are unaffected)."
                )
            return
        if self._previous is not None:
            self._previous(loop, context)
        else:
            loop.default_exception_handler(context)


def _is_stun_retry_teardown_noise(context: dict) -> bool:
    """True only for aioice's post-close ``Transaction.__retry`` AttributeError.

    Matched narrowly (callback identity + exception type) so real failures —
    including real errors raised elsewhere — are never swallowed.
    """
    message = context.get("message", "") or ""
    handle = repr(context.get("handle", ""))
    if "Transaction.__retry" not in message and "Transaction.__retry" not in handle:
        return False
    return isinstance(context.get("exception"), AttributeError)


def install_stun_retry_filter(loop: asyncio.AbstractEventLoop) -> _StunRetryFilter:
    """Install the filter on ``loop`` and return it (exposes ``.suppressed``)."""
    handler = _StunRetryFilter(loop)
    loop.set_exception_handler(handler)
    return handler
