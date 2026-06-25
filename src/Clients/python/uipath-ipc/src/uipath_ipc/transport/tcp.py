"""TCP client and server transports."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass

from ._retry import retry_connect
from .base import ClientTransport, ConnectionHandler, ServerHandle, ServerTransport


@dataclass(frozen=True, slots=True)
class TcpClientTransport(ClientTransport):
    """Client transport over TCP.

    Attributes:
        host: Hostname or IP address.
        port: TCP port.
    """

    host: str
    port: int

    async def connect(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        # Retry ConnectionRefused (the peer's accept loop isn't bound yet) so a
        # TCP client rides out the startup / reconnect-after-restart races, like
        # .NET's TcpClientTransport and this port's named-pipe transport. Bounded
        # by the caller's deadline (the proxy wraps connect+send in wait_for).
        return await retry_connect(
            lambda: asyncio.open_connection(self.host, self.port),
            (ConnectionRefusedError,),
        )


@dataclass(frozen=True, slots=True)
class TcpServerTransport(ServerTransport):
    """Server transport over TCP.

    Attributes:
        host: Interface to bind (e.g. ``"127.0.0.1"``).
        port: TCP port to listen on. Use ``0`` to let the OS pick a free
            port (read it back from the returned ``asyncio.Server`` sockets).
    """

    host: str
    port: int

    async def serve(self, on_connection: ConnectionHandler) -> ServerHandle:
        return await asyncio.start_server(
            lambda r, w: on_connection(r, w), self.host, self.port
        )
