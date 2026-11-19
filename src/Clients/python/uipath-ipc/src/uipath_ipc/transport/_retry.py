"""Shared connect-retry for client transports.

A client often dials during the server's connect/accept warm-up window (the
first call before the listener binds) or while it re-binds after a restart
(auto-reconnect). Like .NET, the client rides those races out by retrying the
*transient* connect errors until the caller gives up: a proxy call dials under
its request timeout (the proxy wraps connect+send in `asyncio.wait_for`), the
way .NET dials under the call's timeout token. With no deadline, it retries
until cancelled, as .NET does with an infinite request timeout.
"""

from __future__ import annotations

import asyncio
from typing import Awaitable, Callable, TypeVar

_T = TypeVar("_T")

#: Waits (seconds) before successive attempts; the last one repeats until the
#: caller cancels.
CONNECT_RETRY_DELAYS: tuple[float, ...] = (0.0, 0.01, 0.02, 0.05, 0.1)


async def retry_connect(
    connect: Callable[[], Awaitable[_T]],
    transient: tuple[type[BaseException], ...],
) -> _T:
    """Call `connect` until it succeeds, retrying only `transient` errors (the
    startup/reconnect races); any other error is raised at once."""
    attempt = 0
    while True:
        delay = CONNECT_RETRY_DELAYS[min(attempt, len(CONNECT_RETRY_DELAYS) - 1)]
        if delay:
            await asyncio.sleep(delay)
        try:
            return await connect()
        except transient:
            attempt += 1
