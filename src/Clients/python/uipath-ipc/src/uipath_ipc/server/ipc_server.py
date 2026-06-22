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
from ..hooks import BeforeCallHandler
from ..transport.base import ServerHandle, ServerTransport


class IpcServer:
    """Hosts services over a `ServerTransport`, one connection per client."""

    def __init__(
        self,
        transport: ServerTransport,
        services: dict[type, object],
        request_timeout: float | None = None,
        before_call: BeforeCallHandler | None = None,
    ) -> None:
        """Create a server.

        Args:
            transport: The listener (named pipe, TCP, ...).
            services: Maps contract type → instance. The instance's method
                names must match the contract's. The instance's class need NOT
                inherit from the contract (duck-typed). The contract's
                ``__name__`` is the endpoint on the wire — matching how
                `IpcClient.get_proxy` names calls. Handler methods must be
                ``async def``: a synchronous handler runs inline on the event
                loop, blocking the whole connection for its duration and
                escaping the request timeout.
            request_timeout: Server-side default bound for an inbound handler
                when the caller's Request carries no explicit timeout (the
                analog of .NET's ``IpcServer.RequestTimeout``); also the default
                for reach-back proxies a hosted service builds via
                ``message.client.get_callback(...)``. ``None`` (default)
                disables both. An explicit per-call wire timeout overrides it.
            before_call: Optional hook awaited before each INCOMING call is
                dispatched to a service — the analog of .NET's
                ``BeforeIncomingCall``. Receives a `CallInfo`; raising aborts
                the call (surfaced to the caller as an error).
        """
        self._transport = transport
        # Translate contract-type keys to endpoint-name keys once; the
        # connection dispatches incoming requests by endpoint name.
        self._services: dict[str, tuple[type, object]] = {}
        for contract, instance in services.items():
            name = contract.__name__
            if name in self._services:
                raise ValueError(
                    f"duplicate endpoint name {name!r}: two contracts share "
                    f"__name__ (the wire endpoint) and would collide"
                )
            self._services[name] = (contract, instance)
        self._request_timeout = request_timeout
        self._before_call = before_call
        self._handle: ServerHandle | None = None
        # Serializes ALL lifecycle state changes (`_disposed`, `_handle`) so
        # start()/aclose() can't race; held across serve() in start().
        self._lifecycle_lock = asyncio.Lock()
        self._connections: set[IpcConnection] = set()
        self._disposed = False

    # --- lifecycle ---------------------------------------------------------

    async def start(self) -> None:
        """Begin listening. Idempotent and safe under concurrent calls.

        Single-use: once `aclose()` has run, the server cannot be restarted —
        `start()` raises (matching .NET's `ObjectDisposedException`). Create a
        new instance to listen again.
        """
        # Read AND mutate lifecycle state under the lock — and hold it across
        # serve() — so a concurrent aclose() (same lock) can neither slip a
        # listener past the disposed check nor leave one un-closed.
        async with self._lifecycle_lock:
            if self._disposed:
                raise RuntimeError(
                    "IpcServer has been closed and cannot be restarted"
                )
            if self._handle is not None:
                return
            self._handle = await self._transport.serve(self._on_connection)

    def _on_connection(
        self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter
    ) -> None:
        """Accept one client: wrap its stream in a service-hosting connection."""
        conn = IpcConnection(
            reader,
            writer,
            callbacks=self._services,
            request_timeout=self._request_timeout,
            before_incoming_call=self._before_call,
            inbound_request_timeout=self._request_timeout,
            # Bound server-side sends (responses / cancellations) by the same
            # budget so a non-reading client can't wedge the shared writer.
            send_timeout=self._request_timeout,
        )
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
        """Stop listening and close every live connection. The server is
        single-use afterward: a subsequent `start()` raises."""
        # Flip lifecycle state under the lock (serialized with start()), then do
        # the slow teardown outside it so the lock isn't held across awaits.
        async with self._lifecycle_lock:
            self._disposed = True
            handle, self._handle = self._handle, None
            connections = list(self._connections)
            self._connections.clear()
        if handle is not None:
            handle.close()
        # Close connections BEFORE awaiting the listener's wait_closed():
        # asyncio.Server.wait_closed() (Python 3.12+) blocks until every
        # active connection has finished, so it would hang otherwise.
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
