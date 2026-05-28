"""TCP client transport."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass

from .base import ClientTransport


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
