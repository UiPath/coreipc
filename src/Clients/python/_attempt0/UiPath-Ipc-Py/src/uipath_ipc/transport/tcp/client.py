"""TCP client transport using asyncio.open_connection."""

from __future__ import annotations

import asyncio

from ..base import ClientTransport


class TcpClientTransport(ClientTransport):
    """Client transport over TCP/IP."""

    def __init__(self, host: str = "127.0.0.1", port: int = 0) -> None:
        self.host = host
        self.port = port

    async def connect(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        return await asyncio.open_connection(self.host, self.port)

    def __str__(self) -> str:
        return f"TcpClient={self.host}:{self.port}"
