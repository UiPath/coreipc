"""The `Message` type and `IClient` handle for handler-initiated reach-back.

Mirrors .NET CoreIpc's `Message`/`Message<T>` and `IClient`:

  - A service or callback method opts into a handle on its *caller's*
    connection by declaring a parameter annotated `Message` (or
    `Message[T]`). The dispatcher injects it; the wire never carries it,
    exactly like .NET's trailing-`Message` convention.
  - From it, ``message.client.get_callback(SomeContract)`` returns a proxy
    that calls back to that same peer over the same duplex connection — the
    inverse direction of the in-flight request. This is the Python analog of
    ``m.Client.GetCallback<SomeContract>()``.

Example — a server-hosted service calling its specific client back::

    class Orchestrator:
        async def Run(self, job_id: str, m: Message) -> None:
            sink = m.client.get_callback(IJobStatusSink)
            await sink.Report(job_id, "started")

The same works for a client-hosted callback reaching back to the server:
both directions are dispatched through the one symmetric `IpcConnection`.
"""

from __future__ import annotations

from typing import Generic, Protocol, TypeVar

T = TypeVar("T")


class IClient(Protocol):
    """The caller's side of a duplex connection, seen from a handler.

    Structurally satisfied by `IpcConnection`; mirrors .NET's `IClient`.
    """

    def get_callback(self, contract: type[T]) -> T:
        """Return a proxy that calls `contract` back over this connection."""
        ...


class Message(Generic[T]):
    """Injected handle to the caller — and, for `Message[T]`, a typed payload.

    Declare a parameter of this type on a service or callback method to
    receive the caller's connection as ``.client`` (and the connection's
    default ``.request_timeout``). The parameter consumes no wire argument.

    ``Message[T]`` types a ``.payload`` of ``T``; inbound payload binding
    from the wire is a follow-up, so ``.payload`` is populated only when a
    `Message` is constructed explicitly (e.g. by a caller).
    """

    __slots__ = ("payload", "client", "request_timeout")

    def __init__(
        self,
        payload: T | None = None,
        *,
        client: IClient | None = None,
        request_timeout: float | None = None,
    ) -> None:
        self.payload = payload
        self.client = client
        self.request_timeout = request_timeout
