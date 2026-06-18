"""Dynamic proxy that turns Python method calls into IPC requests."""

from __future__ import annotations

import asyncio
import inspect
import json
import weakref
from typing import TYPE_CHECKING, Any, get_type_hints

from ..errors import RemoteException
from ..hooks import CallInfo
from ..message import INFINITE_REQUEST_TIMEOUT, Message
from ..wire import Request, Response, from_wire, to_wire

if TYPE_CHECKING:
    from .ipc_client import IpcClient


# Cache of a contract method's resolved return annotation, keyed weakly by the
# function so reflection runs once per method.
_return_hint_cache: "weakref.WeakKeyDictionary[Any, Any]" = weakref.WeakKeyDictionary()
#: Distinct cache-miss sentinel so a genuinely-cached `None` (a method with no
#: return annotation) is a hit, not re-resolved via get_type_hints every call.
_NO_HINT = object()


def _return_hint(contract: type, method_name: str) -> Any:
    func = inspect.getattr_static(contract, method_name, None)
    if func is None:
        return None
    cached = _return_hint_cache.get(func, _NO_HINT)
    if cached is not _NO_HINT:
        return cached
    try:
        hint = get_type_hints(func).get("return")
    except Exception:
        hint = None
    try:
        _return_hint_cache[func] = hint
    except TypeError:
        pass
    return hint


def _message_wire(m: Message) -> dict:
    """The wire form of a `Message` argument, matching .NET: a payload-less
    `Message` serializes to `{}`; `Message[T]` to `{"Payload": <payload>}`;
    `wire_body` stands in for a .NET `Message` *subclass* (its fields ARE the
    arg, at top level). `wire_body`/`payload` are run through `to_wire`, so a
    dataclass — or any value type — can be handed in directly with no explicit
    serialization. `client`/`request_timeout` are transport-only (never sent)."""
    if m.wire_body is not None:
        return to_wire(m.wire_body)
    return {} if m.payload is None else {"Payload": to_wire(m.payload)}


class _IpcProxy:
    """Forwards attribute-access method calls as Request frames.

    Created by `IpcClient.get_proxy(contract)`. The contract is typically
    an ABC describing the remote interface; method names and the contract's
    `__name__` (used as the wire endpoint) come from there.

    Each call:
      - takes only positional args (keyword args are not in the .NET wire
        format),
      - encodes each argument with `json.dumps` (so Request.Parameters
        ends up as `list[str]` of already-JSON-encoded values),
      - sends the Request and awaits the matching Response,
      - returns `json.loads(Response.Data)` for non-null Data, else None,
      - raises `RemoteException` if `Response.Error` is set.
    """

    def __init__(self, client: IpcClient, contract: type) -> None:
        # Use object.__setattr__ to bypass our own __getattr__ during init
        object.__setattr__(self, "_client", client)
        object.__setattr__(self, "_contract", contract)
        object.__setattr__(self, "_endpoint_name", contract.__name__)

    def __getattr__(self, name: str) -> Any:
        if name.startswith("_"):
            raise AttributeError(name)

        attr = inspect.getattr_static(self._contract, name, None)
        if attr is None or not callable(attr):
            raise AttributeError(
                f"{self._contract.__name__!r} has no method {name!r}"
            )

        async def call(*args: Any) -> Any:
            return await self._invoke(name, args)

        # Cache on the instance so subsequent accesses bypass __getattr__.
        object.__setattr__(self, name, call)
        return call

    async def _invoke(self, method_name: str, args: tuple[Any, ...]) -> Any:
        # Two DISTINCT timeouts, mirroring .NET ServiceClient.Invoke exactly:
        #
        #   * client_timeout — the LOCAL await deadline (.NET `clientTimeout =
        #     Config.RequestTimeout.OrInfinite()`). Bounds only this client's
        #     own wait; it is NEVER serialized.
        #   * wire_timeout   — what rides the Request envelope as
        #     `TimeoutInSeconds` (.NET `messageTimeout`, which defaults to
        #     TimeSpan.Zero == "use the server's default"). It is set ONLY by
        #     an explicit per-call `Message` timeout.
        #
        # The client-wide `request_timeout` must NOT leak onto the wire: in
        # .NET it dictates how long THIS client waits, not how long the SERVER
        # spends processing. Sending it as TimeoutInSeconds would silently
        # impose the caller's local SLA as the server's cancellation deadline.
        client_timeout = self._client.request_timeout
        wire_timeout: float | None = None
        params: list[str] = []
        for a in args:
            if isinstance(a, Message):
                # .NET: `case Message { RequestTimeout: var rt } when rt !=
                # TimeSpan.Zero`. An explicit per-call timeout sets BOTH the
                # wire value AND the local deadline (the two coincide); rt == 0
                # is the "no override" sentinel, not a real timeout.
                if a.request_timeout is not None and a.request_timeout != 0:
                    wire_timeout = a.request_timeout
                    client_timeout = a.request_timeout
                params.append(json.dumps(_message_wire(a)))
            else:
                # to_wire encodes value types (bytes->base64, UUID/datetime/
                # Decimal/enum/dataclass) and is a no-op for plain JSON values,
                # so existing primitive/dict args are unchanged.
                params.append(json.dumps(to_wire(a)))
        # Normalize a negative per-call timeout to the infinite sentinel: .NET
        # treats only -1ms (INFINITE_REQUEST_TIMEOUT = -0.001) as
        # Timeout.InfiniteTimeSpan and rejects other negatives. This applies to
        # the WIRE value only; the local deadline below treats any non-positive
        # value as unbounded.
        if wire_timeout is not None and wire_timeout < 0:
            wire_timeout = INFINITE_REQUEST_TIMEOUT
        async def _connect_and_send() -> Response:
            # The dial shares the call deadline (see below) — a black-holed or
            # unreachable host no longer escapes `request_timeout` via an
            # unbounded connect. Mirrors .NET flowing one TimeoutHelper token
            # into connect+send.
            conn, created = await self._client._ensure_connected()
            # BeforeCall hook (client only — a reach-back proxy is bound to a
            # bare connection, which has no `before_call`, so callbacks skip it).
            before_call = getattr(self._client, "before_call", None)
            if before_call is not None:
                result = before_call(
                    CallInfo(
                        self._endpoint_name, method_name, args,
                        new_connection=created,
                    )
                )
                if inspect.isawaitable(result):
                    await result
            req = Request(
                endpoint=self._endpoint_name,
                method_name=method_name,
                parameters=params,
                id=conn.next_id(),
                timeout_in_seconds=wire_timeout,
            )
            return await conn.send_request(req)

        # Bound connect+send by the LOCAL deadline (client_timeout). A
        # non-positive value imposes no client-side deadline: 0 ("use server
        # default" — never an instant wait_for(0)) and a negative value (.NET's
        # Timeout.InfiniteTimeSpan) both pass through unbounded. This is the
        # client's own SLA and is independent of what rode the wire.
        if client_timeout is not None and client_timeout > 0:
            resp = await asyncio.wait_for(_connect_and_send(), timeout=client_timeout)
        else:
            resp = await _connect_and_send()
        if resp.error is not None:
            raise RemoteException.from_error(resp.error)
        # Void / fire-and-forget operations answer with an empty Data string
        # (not null) — e.g. .NET CoreIpc's response for a `Task`-returning
        # method. Treat empty (or null) Data as "no return value".
        if not resp.data:
            return None
        parsed = json.loads(resp.data)
        # Materialize into the contract's declared return type (reflection),
        # like .NET handing Newtonsoft `typeof(TResult)`: dataclasses, enums,
        # and scalar value types (bytes/UUID/datetime/Decimal) — and containers
        # of those — are built into instances, so a typed contract yields typed
        # results with no explicit decode step. A `dict`/`Any`/unannotated
        # return passes through as the raw parsed structure.
        return from_wire(parsed, _return_hint(self._contract, method_name))
