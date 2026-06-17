"""User-facing IpcClient: owns one connection, hands out typed proxies."""

from __future__ import annotations

import asyncio
import inspect
import warnings
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
        self._closed = False
        #: The task currently establishing a connection (holds _connect_lock),
        #: so a re-entrant _ensure_connected from a before_connect hook is caught
        #: rather than deadlocking.
        self._connecting_task: asyncio.Task | None = None
        self.request_timeout = request_timeout
        self._before_connect = before_connect
        #: Read by `_IpcProxy._invoke` before sending each outgoing request.
        self.before_call = before_call
        # Translate contract-type keys to endpoint-name keys once at
        # construction; the connection stores by name but keeps the contract
        # type so dispatch can resolve incoming methods against it.
        self._callbacks: dict[str, tuple[type, object]] = {}
        if callbacks:
            for contract_type, instance in callbacks.items():
                self._callbacks[contract_type.__name__] = (contract_type, instance)

    async def _ensure_connected(self) -> IpcConnection:
        if self._connection is not None and not self._connection.is_closed:
            return self._connection
        # Reentrancy guard: a before_connect hook that calls back into this same
        # client runs in the task already holding _connect_lock, so re-acquiring
        # it would deadlock silently. Fail loudly with a clear message instead.
        if (
            self._connecting_task is not None
            and self._connecting_task is asyncio.current_task()
        ):
            raise RuntimeError(
                "before_connect must not make calls on the same IpcClient — it "
                "runs while the connection is being established"
            )
        async with self._connect_lock:
            # Serialized with aclose(): if the client was closed (possibly while
            # we waited for the lock), don't silently re-dial — the connection a
            # concurrent aclose() expected to be gone would otherwise reappear.
            if self._closed:
                raise ConnectionError("client is closed")
            if self._connection is not None and not self._connection.is_closed:
                return self._connection
            # Tear down the dead connection (no-op if already cleaned up)
            # before re-dialing through the transport.
            if self._connection is not None:
                await self._connection.aclose()
            self._connecting_task = asyncio.current_task()
            try:
                if self._before_connect is not None:
                    result = self._before_connect()
                    if inspect.isawaitable(result):
                        await result
                self._connection = await IpcConnection.open(
                    self._transport,
                    callbacks=self._callbacks,
                    request_timeout=self.request_timeout,
                )
            finally:
                self._connecting_task = None
        return self._connection

    def get_proxy(self, contract: type[T]) -> T:
        """Return a proxy that looks like an instance of `contract`."""
        return cast(T, _IpcProxy(self, contract))

    async def aclose(self) -> None:
        # Take the connect lock so close can't race an in-flight connect (which
        # would otherwise complete and assign a live connection *after* close
        # returned — a leak / use-after-close).
        async with self._connect_lock:
            self._closed = True
            if self._connection is not None:
                await self._connection.aclose()
                self._connection = None

    async def __aenter__(self) -> IpcClient:
        return self

    async def __aexit__(self, *exc_info: object) -> None:
        await self.aclose()

    def __del__(self) -> None:
        # A client dropped without aclose() would otherwise leak: its
        # connection's receive-loop task keeps the connection (and the socket)
        # alive forever. Warn and best-effort close the writer so the task
        # unblocks and ends. 'async with IpcClient(...)' / aclose() is the
        # supported path; this is just a safety net.
        conn = getattr(self, "_connection", None)
        if conn is None or conn.is_closed:
            return
        try:
            warnings.warn(
                f"{type(self).__name__} was garbage-collected without aclose(); "
                "use 'async with' or call aclose().",
                ResourceWarning,
                stacklevel=2,
            )
            conn._abandon()
        except Exception:
            pass
