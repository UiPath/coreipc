"""Utility helpers for timeout management and async operations."""

from __future__ import annotations

import asyncio
from typing import Any


class TimeoutHelper:
    """Manages combined timeout + external cancellation, mirroring the .NET TimeoutHelper."""

    def __init__(self, timeout_seconds: float | None, cancel_event: asyncio.Event | None = None) -> None:
        self._timeout = timeout_seconds
        self._cancel_event = cancel_event
        self._timed_out = False

    async def apply(self, coro: Any) -> Any:
        """Run a coroutine with the configured timeout. Raises TimeoutError on expiry."""
        if self._timeout is None or self._timeout <= 0:
            return await coro

        try:
            return await asyncio.wait_for(coro, timeout=self._timeout)
        except asyncio.TimeoutError:
            self._timed_out = True
            raise TimeoutError(f"Operation timed out after {self._timeout}s.")

    @property
    def timed_out(self) -> bool:
        return self._timed_out
