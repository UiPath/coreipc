"""Shared bounded connect-retry for client transports.

A client often dials during the server's connect/accept warm-up window (the
first call before the listener binds) or while it re-binds after a restart
(auto-reconnect). The framework is meant to ride those races out with a brief
backoff, retrying only the *transient* connect errors. .NET does this for TCP
(`ConnectionRefused`, 10 ms backoff) and the named-pipe warm-up alike; this is
the one helper both Python transports share so they behave the same.

The overall call should still be bounded by the caller's deadline (the proxy
wraps connect+send in `asyncio.wait_for`), so this ladder is finite and short.
"""

from __future__ import annotations

import asyncio
from typing import Awaitable, Callable, TypeVar

_T = TypeVar("_T")

#: Backoff ladder (seconds) across connect attempts; ~1.85s total.
CONNECT_RETRY_DELAYS: tuple[float, ...] = (0.0, 0.05, 0.1, 0.2, 0.5, 1.0)


async def retry_connect(
    connect: Callable[[], Awaitable[_T]],
    transient: tuple[type[BaseException], ...],
) -> _T:
    """Call `connect` over `CONNECT_RETRY_DELAYS`, retrying only `transient`
    errors (the startup/reconnect races) and raising the last one if every
    attempt fails."""
    last: BaseException | None = None
    for delay in CONNECT_RETRY_DELAYS:
        if delay:
            await asyncio.sleep(delay)
        try:
            return await connect()
        except transient as ex:
            last = ex
    assert last is not None  # the loop body ran at least once
    raise last
