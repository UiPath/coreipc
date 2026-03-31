"""IpcServer: main server entry point, mirroring IpcServer.cs."""

from __future__ import annotations

import asyncio
import logging
from typing import Any

from ..transport.base import ServerState, ServerTransport
from .contract import ContractCollection
from .router import Router
from .server_connection import ServerConnection

logger = logging.getLogger(__name__)

_connection_counter = 0


class IpcServer:
    """IPC server that accepts connections and dispatches RPC requests.

    Usage::

        endpoints = ContractCollection()
        endpoints.add(IMyService, MyServiceImpl())

        async with IpcServer(
            transport=TcpServerTransport("127.0.0.1", 5050),
            endpoints=endpoints,
        ) as server:
            await server.wait_closed()
    """

    def __init__(
        self,
        transport: ServerTransport,
        endpoints: ContractCollection,
        request_timeout: float | None = None,
    ) -> None:
        self._transport = transport
        self._endpoints = endpoints
        self._request_timeout = request_timeout

        self._router_config = Router.build_config(endpoints)
        self._server_state: ServerState | None = None
        self._accept_tasks: list[asyncio.Task[None]] = []
        self._connection_tasks: set[asyncio.Task[None]] = set()
        self._shutdown_event = asyncio.Event()
        self._started = False

    @property
    def transport(self) -> ServerTransport:
        return self._transport

    async def start(self) -> None:
        """Start accepting connections."""
        if self._started:
            return
        self._started = True
        self._server_state = await self._transport.create_server_state()
        for _ in range(self._transport.concurrent_accepts):
            task = asyncio.create_task(self._accept_loop())
            self._accept_tasks.append(task)
        logger.info("IpcServer started on %s", self._transport)

    async def close(self) -> None:
        """Stop accepting and close all connections."""
        self._shutdown_event.set()

        if self._server_state:
            await self._server_state.close()

        for task in self._accept_tasks:
            task.cancel()

        if self._accept_tasks:
            await asyncio.gather(*self._accept_tasks, return_exceptions=True)

        # Wait for active connections to finish
        if self._connection_tasks:
            for task in self._connection_tasks:
                task.cancel()
            await asyncio.gather(*self._connection_tasks, return_exceptions=True)

        self._started = False
        logger.info("IpcServer stopped.")

    async def wait_closed(self) -> None:
        """Wait until the server is shut down."""
        await self._shutdown_event.wait()

    async def _accept_loop(self) -> None:
        while not self._shutdown_event.is_set():
            try:
                reader, writer = await self._server_state.accept()  # type: ignore[union-attr]
                self._on_new_connection(reader, writer)
            except asyncio.CancelledError:
                break
            except Exception as ex:
                logger.error("Failed to accept connection: %s", ex)

    def _on_new_connection(
        self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter
    ) -> None:
        global _connection_counter
        _connection_counter += 1
        debug_name = f"ServerConnection #{_connection_counter}"

        router = Router(self._router_config, debug_name)
        conn = ServerConnection(
            reader,
            writer,
            router,
            self._request_timeout,
            debug_name=debug_name,
            max_message_size=self._transport.max_message_size,
        )
        task = asyncio.create_task(conn.listen())
        self._connection_tasks.add(task)
        task.add_done_callback(self._connection_tasks.discard)

    # Context manager support
    async def __aenter__(self) -> IpcServer:
        await self.start()
        return self

    async def __aexit__(self, *args: Any) -> None:
        await self.close()
