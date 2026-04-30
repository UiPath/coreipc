from __future__ import annotations

import asyncio
from abc import ABC, abstractmethod
from typing import Awaitable, Callable

OnConnection = Callable[[asyncio.StreamReader, asyncio.StreamWriter], Awaitable[None]]


class ClientTransport(ABC):
    @abstractmethod
    async def connect(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        ...


class ServerTransport(ABC):
    @abstractmethod
    async def serve(self, on_connection: OnConnection) -> "ServerHandle":
        ...


class ServerHandle(ABC):
    @abstractmethod
    async def close(self) -> None:
        ...

    @abstractmethod
    async def wait_closed(self) -> None:
        ...
