"""Abstract base classes for server and client transports."""

from __future__ import annotations

import asyncio
from abc import ABC, abstractmethod


class ServerState(ABC):
    """Represents a listening server that can accept connections."""

    @abstractmethod
    async def accept(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        """Wait for and accept a new connection."""
        ...

    @abstractmethod
    async def close(self) -> None:
        """Stop accepting and release resources."""
        ...


class ServerTransport(ABC):
    """Abstract base for server-side transports."""

    concurrent_accepts: int = 5
    max_received_message_size_mb: int = 2

    @property
    def max_message_size(self) -> int:
        return self.max_received_message_size_mb * 1024 * 1024

    @abstractmethod
    async def create_server_state(self) -> ServerState:
        """Create the listening state for this transport."""
        ...


class ClientTransport(ABC):
    """Abstract base for client-side transports."""

    @abstractmethod
    async def connect(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        """Establish a connection and return the stream pair."""
        ...
