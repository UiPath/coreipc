"""User-facing IpcClient: owns one connection, hands out typed proxies."""

from __future__ import annotations

import asyncio
import inspect
from typing import TypeVar, cast

from ..hooks import BeforeCallHandler, BeforeConnectHandler
from ..transport.base import ClientTransport
from .connection import IpcConnection
from .proxy import _IpcProxy

T = TypeVar("T")


class IpcClient:
    """Client-side handle to an IPC server.

    Holds one `IpcConnection` (opened lazily on first call), and produces
    interface proxies via `get_proxy(SomeContract)`.

    Example::

        async with IpcClient(transport=NamedPipeClientTransport("test")) as client:
            svc = client.get_proxy(IComputingService)
            result = await svc.AddFloats(1.5, 2.5)
    """

    def __init__(
        self,
        transport: ClientTransport,
        request_timeout: float | None = None,
        callbacks: dict[type, object] | None = None,
        before_connect: BeforeConnectHandler | None = None,
        before_call: BeforeCallHandler | None = None,
    ) -> None:
        """Create a new client.

        Args:
            transport: The transport that opens the underlying stream.
            request_timeout: Seconds before an in-flight call gives up.
                Applies both client-side (raises asyncio.TimeoutError) and
                server-side (Request.TimeoutInSeconds). ``None`` (default)
                disables both timeouts. A per-call timeout can override this
                via a ``Message`` argument.
            callbacks: Optional dict mapping contract type → instance for
                server-to-client callbacks. The instance's method names
                must match the contract's; each method may be ``async``.
                The instance's class need NOT inherit from the contract
                (duck-typed). The contract's ``__name__`` is what's used
                as the endpoint on the wire.
            before_connect: Optional hook awaited before each (re)connect —
                the analog of .NET's ``BeforeConnect``. Sync or async; if it
                raises, the connect fails.
            before_call: Optional hook awaited before each OUTGOING call (not
                for inbound callbacks) — the analog of .NET's
                ``BeforeOutgoingCall``. Receives a `CallInfo`; raising aborts
                the call.
        """
        self._transport = transport
        self._connection: IpcConnection | None = None
        self._connect_lock = asyncio.Lock()
        self.request_timeout = request_timeout
        self._before_connect = before_connect
        #: Read by `_IpcProxy._invoke` before sending each outgoing request.
        self.before_call = before_call
        # Translate contract-type keys to endpoint-name keys once at
        # construction; the connection stores by name.
        self._callbacks: dict[str, object] = {}
        if callbacks:
            for contract_type, instance in callbacks.items():
                self._callbacks[contract_type.__name__] = instance

    async def _ensure_connected(self) -> IpcConnection:
        if self._connection is not None and not self._connection.is_closed:
            return self._connection
        async with self._connect_lock:
            if self._connection is not None and not self._connection.is_closed:
                return self._connection
            # Tear down the dead connection (no-op if already cleaned up)
            # before re-dialing through the transport.
            if self._connection is not None:
                await self._connection.aclose()
            if self._before_connect is not None:
                result = self._before_connect()
                if inspect.isawaitable(result):
                    await result
            self._connection = await IpcConnection.open(
                self._transport,
                callbacks=self._callbacks,
                request_timeout=self.request_timeout,
            )
        return self._connection

    def get_proxy(self, contract: type[T]) -> T:
        """Return a proxy that looks like an instance of `contract`."""
        return cast(T, _IpcProxy(self, contract))

    async def aclose(self) -> None:
        if self._connection is not None:
            await self._connection.aclose()
            self._connection = None

    async def __aenter__(self) -> IpcClient:
        return self

    async def __aexit__(self, *exc_info: object) -> None:
        await self.aclose()
