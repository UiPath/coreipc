"""Abstract base for client transports."""

from __future__ import annotations

import asyncio
from abc import ABC, abstractmethod


class ClientTransport(ABC):
    """Establishes a duplex stream to an IPC server.

    Concrete implementations (named pipe, TCP, websocket, ...) return a
    matched `(StreamReader, StreamWriter)` pair the connection layer
    drives. Transport instances are reusable: each call to `connect`
    establishes a fresh stream.
    """

    @abstractmethod
    async def connect(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        """Open a new duplex stream to the server."""
        ...
