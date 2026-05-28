"""User-facing IpcClient: owns one connection, hands out typed proxies."""

from __future__ import annotations

import asyncio
from typing import TypeVar, cast

from ..transport.base import ClientTransport
from .connection import IpcConnection
from .proxy import _IpcProxy

T = TypeVar("T")


class IpcClient:
    """Client-side handle to an IPC server.

    Holds one `IpcConnection` (opened lazily on first call), and produces
    interface proxies via `get_proxy(SomeContract)`.

    Example::

        async with IpcClient(transport=NamedPipeClientTransport("test")) as client:
            svc = client.get_proxy(IComputingService)
            result = await svc.AddFloats(1.5, 2.5)
    """

    def __init__(
        self,
        transport: ClientTransport,
        request_timeout: float | None = None,
    ) -> None:
        """Create a new client.

        Args:
            transport: The transport that opens the underlying stream.
            request_timeout: Seconds before an in-flight call gives up.
                Applies both client-side (raises asyncio.TimeoutError) and
                server-side (Request.TimeoutInSeconds). ``None`` (default)
                disables both timeouts.
        """
        self._transport = transport
        self._connection: IpcConnection | None = None
        self._connect_lock = asyncio.Lock()
        self.request_timeout = request_timeout

    async def _ensure_connected(self) -> IpcConnection:
        if self._connection is not None and not self._connection.is_closed:
            return self._connection
        async with self._connect_lock:
            if self._connection is None or self._connection.is_closed:
                self._connection = await IpcConnection.open(self._transport)
        return self._connection

    def get_proxy(self, contract: type[T]) -> T:
        """Return a proxy that looks like an instance of `contract`."""
        return cast(T, _IpcProxy(self, contract))

    async def aclose(self) -> None:
        if self._connection is not None:
            await self._connection.aclose()
            self._connection = None

    async def __aenter__(self) -> IpcClient:
        return self

    async def __aexit__(self, *exc_info: object) -> None:
        await self.aclose()
