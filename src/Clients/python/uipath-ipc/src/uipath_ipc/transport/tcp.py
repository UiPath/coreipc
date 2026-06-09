"""TCP client and server transports."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass

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
        return await asyncio.open_connection(self.host, self.port)


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
