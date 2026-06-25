"""Abstract bases for client and server transports."""

from __future__ import annotations

import asyncio
from abc import ABC, abstractmethod
from typing import Callable, Protocol

#: Called once per accepted connection with its (reader, writer) pair.
ConnectionHandler = Callable[
    [asyncio.StreamReader, asyncio.StreamWriter], object
]


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


class ServerHandle(Protocol):
    """A running listener. Both `asyncio.Server` and our pipe-server wrapper
    satisfy this structurally."""

    def close(self) -> None: ...
    async def wait_closed(self) -> None: ...


class ServerTransport(ABC):
    """Listens for incoming duplex streams.

    `serve(on_connection)` starts listening and invokes `on_connection`
    with the `(reader, writer)` pair of each accepted client, returning a
    handle that stops the listener when closed.
    """

    @abstractmethod
    async def serve(self, on_connection: ConnectionHandler) -> ServerHandle:
        """Begin accepting connections; return a handle to stop listening."""
        ...
