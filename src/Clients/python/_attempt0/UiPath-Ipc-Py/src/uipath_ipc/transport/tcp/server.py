"""TCP server transport using asyncio.start_server."""

from __future__ import annotations

import asyncio

from ..base import ServerState, ServerTransport


class TcpServerState(ServerState):
    def __init__(self, server: asyncio.Server, queue: asyncio.Queue[tuple[asyncio.StreamReader, asyncio.StreamWriter]]) -> None:
        self._server = server
        self._queue = queue

    async def accept(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        return await self._queue.get()

    async def close(self) -> None:
        self._server.close()
        await self._server.wait_closed()


class TcpServerTransport(ServerTransport):
    """Server transport over TCP/IP."""

    def __init__(self, host: str = "127.0.0.1", port: int = 0) -> None:
        self.host = host
        self.port = port

    async def create_server_state(self) -> TcpServerState:
        queue: asyncio.Queue[tuple[asyncio.StreamReader, asyncio.StreamWriter]] = asyncio.Queue()

        def on_connection(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
            queue.put_nowait((reader, writer))

        server = await asyncio.start_server(on_connection, self.host, self.port, backlog=self.concurrent_accepts)
        # Update port if it was auto-assigned (port=0)
        sockets = server.sockets
        if sockets:
            self.port = sockets[0].getsockname()[1]
        return TcpServerState(server, queue)

    def __str__(self) -> str:
        return f"TcpServer={self.host}:{self.port}"
