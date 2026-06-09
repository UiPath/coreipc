"""User-facing IpcServer: listens, and hosts services on each connection.

A server is a thin listen/accept layer over the existing symmetric
`IpcConnection`. Each accepted client gets its own `IpcConnection` whose
``callbacks`` dict is the set of hosted services: an incoming Request for
endpoint ``Foo`` method ``Bar`` is dispatched to ``services[Foo].Bar(*args)``.

Because the connection is duplex, a hosted service can also call *back* into
the connected client — but issuing those outbound calls from inside a handler
needs a per-connection handle, which is a follow-up. This class covers the
inbound direction: Python hosting services that a (.NET or Python) client calls.

Example::

    class Calculator:
        async def Add(self, a: float, b: float) -> float:
            return a + b

    server = IpcServer(
        transport=NamedPipeServerTransport("calc"),
        services={ICalculator: Calculator()},
    )
    async with server:
        await server.serve_forever()
"""

from __future__ import annotations

import asyncio

from ..client.connection import IpcConnection
from ..transport.base import ServerHandle, ServerTransport


class IpcServer:
    """Hosts services over a `ServerTransport`, one connection per client."""

    def __init__(
        self,
        transport: ServerTransport,
        services: dict[type, object],
    ) -> None:
        """Create a server.

        Args:
            transport: The listener (named pipe, TCP, ...).
            services: Maps contract type → instance. The instance's method
                names must match the contract's; each may be ``async``. The
                instance's class need NOT inherit from the contract
                (duck-typed). The contract's ``__name__`` is the endpoint on
                the wire — matching how `IpcClient.get_proxy` names calls.
        """
        self._transport = transport
        # Translate contract-type keys to endpoint-name keys once; the
        # connection dispatches incoming requests by endpoint name.
        self._services: dict[str, object] = {
            contract.__name__: instance for contract, instance in services.items()
        }
        self._handle: ServerHandle | None = None
        self._connections: set[IpcConnection] = set()

    # --- lifecycle ---------------------------------------------------------

    async def start(self) -> None:
        """Begin listening. Idempotent."""
        if self._handle is not None:
            return
        self._handle = await self._transport.serve(self._on_connection)

    def _on_connection(
        self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter
    ) -> None:
        """Accept one client: wrap its stream in a service-hosting connection."""
        conn = IpcConnection(reader, writer, callbacks=self._services)
        self._connections.add(conn)
        # Prune from the live set when the peer disconnects or we close it.
        conn.add_close_callback(self._connections.discard)
        conn.start()

    async def serve_forever(self) -> None:
        """Block until the listener is closed (e.g. by `aclose`)."""
        if self._handle is None:
            raise RuntimeError("server not started")
        await self._handle.wait_closed()

    async def aclose(self) -> None:
        """Stop listening and close every live connection."""
        handle, self._handle = self._handle, None
        if handle is not None:
            handle.close()
        # Close connections BEFORE awaiting the listener's wait_closed():
        # asyncio.Server.wait_closed() (Python 3.12+) blocks until every
        # active connection has finished, so it would hang otherwise.
        connections = list(self._connections)
        self._connections.clear()
        for conn in connections:
            await conn.aclose()
        if handle is not None:
            try:
                await handle.wait_closed()
            except Exception:
                pass

    # --- introspection -----------------------------------------------------

    @property
    def handle(self) -> ServerHandle | None:
        """The underlying listener (e.g. for `asyncio.Server.sockets`)."""
        return self._handle

    @property
    def connection_count(self) -> int:
        """Number of currently live client connections."""
        return len(self._connections)

    async def __aenter__(self) -> IpcServer:
        await self.start()
        return self

    async def __aexit__(self, *exc_info: object) -> None:
        await self.aclose()
