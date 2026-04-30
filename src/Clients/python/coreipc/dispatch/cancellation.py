from __future__ import annotations

import asyncio
from typing import Callable


class CancellationToken:
    """Read-only view of a CancellationTokenSource's state. Mirrors the TS BCL shape."""

    def __init__(self, source: "CancellationTokenSource") -> None:
        self._source = source

    @property
    def is_cancelled(self) -> bool:
        return self._source._event.is_set()

    def throw_if_cancelled(self) -> None:
        if self.is_cancelled:
            raise asyncio.CancelledError()

    async def wait_cancelled(self) -> None:
        await self._source._event.wait()

    def register(self, callback: Callable[[], None]) -> Callable[[], None]:
        """Attach a fire-once callback. Returns an unsubscribe callable.

        The callback runs synchronously from cancel() — keep it cheap and exception-safe.
        If the token is already cancelled, the callback is scheduled on the current loop.
        """
        if self.is_cancelled:
            try:
                loop = asyncio.get_running_loop()
                loop.call_soon(callback)
            except RuntimeError:
                callback()
            return lambda: None
        self._source._callbacks.append(callback)

        def unsubscribe() -> None:
            try:
                self._source._callbacks.remove(callback)
            except ValueError:
                pass

        return unsubscribe


class CancellationTokenSource:
    """Mirror of System.Threading.CancellationTokenSource / TS CancellationTokenSource."""

    def __init__(self) -> None:
        self._event = asyncio.Event()
        self._callbacks: list[Callable[[], None]] = []
        self.token = CancellationToken(self)

    @property
    def is_cancellation_requested(self) -> bool:
        return self._event.is_set()

    def cancel(self) -> None:
        if self._event.is_set():
            return
        self._event.set()
        callbacks, self._callbacks = self._callbacks, []
        for cb in callbacks:
            try:
                cb()
            except Exception:
                pass

    @classmethod
    def with_timeout(cls, seconds: float) -> "CancellationTokenSource":
        cts = cls()
        loop = asyncio.get_event_loop()
        loop.call_later(seconds, cts.cancel)
        return cts

    @classmethod
    def from_event(cls, event: asyncio.Event) -> "CancellationTokenSource":
        """Bridge an existing asyncio.Event into a CancellationTokenSource.

        When `event` is set, the returned CTS is cancelled. Used server-side to expose the
        incoming-request cancel signal as a standard CancellationToken to handlers.
        """
        cts = cls()
        if event.is_set():
            cts.cancel()
            return cts

        async def _bridge() -> None:
            try:
                await event.wait()
            except asyncio.CancelledError:
                return
            cts.cancel()

        asyncio.create_task(_bridge())
        return cts
