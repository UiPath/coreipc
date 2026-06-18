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
            request_timeout: Seconds before an in-flight call gives up. This is
                a LOCAL deadline only — it bounds how long this client waits
                (raising asyncio.TimeoutError) and is NEVER serialized onto the
                wire, mirroring .NET's `clientTimeout`. It does not dictate the
                server's processing deadline. ``None`` (default) imposes no
                local deadline. To set the wire ``Request.TimeoutInSeconds``
                (the server-side deadline) for a single call, pass a per-call
                ``Message`` with ``request_timeout`` — that value overrides the
                local deadline for that call and is the only thing that rides
                the wire.
            callbacks: Optional dict mapping contract type → instance for
                server-to-client callbacks. The instance's method names
                must match the contract's. The instance's class need NOT
                inherit from the contract (duck-typed). The contract's
                ``__name__`` is what's used as the endpoint on the wire.
                Handler methods must be ``async def``: a synchronous handler
                runs inline on the event loop, blocking the whole connection
                for its duration and escaping the request timeout.
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
                name = contract_type.__name__
                if name in self._callbacks:
                    raise ValueError(
                        f"duplicate callback endpoint name {name!r}: two "
                        f"contracts share __name__ and would collide"
                    )
                self._callbacks[name] = (contract_type, instance)

    async def _ensure_connected(self) -> tuple[IpcConnection, bool]:
        """Return (connection, created); created is True iff this call opened a
        fresh connection (surfaced to before_call as CallInfo.new_connection)."""
        if self._connection is not None and not self._connection.is_closed:
            return self._connection, False
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
                return self._connection, False
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
        return self._connection, True

    def get_proxy(self, contract: type[T]) -> T:
        """Return a proxy that looks like an instance of `contract`."""
        return cast(T, _IpcProxy(self, contract))

    async def aclose(self) -> None:
        # Serialize with an in-flight connect so close can't be undone by a dial
        # that completes after it returns. Set _closed first (so _ensure_connected
        # bails under the lock), then cancel the connecting task — otherwise
        # aclose() would block forever behind a slow/hung connect that holds
        # _connect_lock with no deadline of its own (request_timeout=None).
        self._closed = True
        ct = self._connecting_task
        if ct is not None and ct is asyncio.current_task():
            raise RuntimeError(
                "aclose() must not be called from a before_connect hook on the "
                "same IpcClient (it runs while the connection is being established)"
            )
        if ct is not None:
            ct.cancel()  # unblock the dial so we can take the lock promptly
        async with self._connect_lock:
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
