"""Lightweight CancellationToken for async cancellation."""

from __future__ import annotations

import asyncio


class CancellationToken:
    """A cancellation token compatible with asyncio."""

    NONE: CancellationToken

    def __init__(self) -> None:
        self._event = asyncio.Event()

    @property
    def is_cancelled(self) -> bool:
        return self._event.is_set()

    def cancel(self) -> None:
        self._event.set()

    def throw_if_cancelled(self) -> None:
        if self.is_cancelled:
            raise asyncio.CancelledError("Operation was cancelled.")

    async def wait(self) -> None:
        """Wait until cancellation is requested."""
        await self._event.wait()


class _NoneCancellationToken(CancellationToken):
    """A token that is never cancelled."""

    def __init__(self) -> None:
        # Skip parent __init__ to avoid creating an Event
        pass

    @property
    def is_cancelled(self) -> bool:
        return False

    def cancel(self) -> None:
        pass

    def throw_if_cancelled(self) -> None:
        pass

    async def wait(self) -> None:
        await asyncio.Event().wait()  # waits forever


CancellationToken.NONE = _NoneCancellationToken()
