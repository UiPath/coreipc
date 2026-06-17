"""Dynamic proxy that turns Python method calls into IPC requests."""

from __future__ import annotations

import asyncio
import inspect
import json
import weakref
from typing import TYPE_CHECKING, Any, get_type_hints

from ..errors import RemoteException
from ..hooks import CallInfo
from ..message import Message
from ..wire import Request, Response, from_wire, to_wire

if TYPE_CHECKING:
    from .ipc_client import IpcClient


# Cache of a contract method's resolved return annotation, keyed weakly by the
# function so reflection runs once per method. `None` means "no usable hint".
_return_hint_cache: "weakref.WeakKeyDictionary[Any, Any]" = weakref.WeakKeyDictionary()


def _return_hint(contract: type, method_name: str) -> Any:
    func = inspect.getattr_static(contract, method_name, None)
    if func is None:
        return None
    cached = _return_hint_cache.get(func)
    if cached is not None:
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
    `wire_body` stands in for a .NET `Message` *subclass* and serializes
    as-is. `client`/`request_timeout` are transport-only (never serialized)."""
    if m.wire_body is not None:
        return m.wire_body
    return {} if m.payload is None else {"Payload": m.payload}


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
        # A `Message` argument may carry a per-call timeout (the .NET/TS
        # mechanism): it overrides the client-wide default for this call only,
        # and is serialized to its wire form rather than dumped as a plain arg.
        timeout = self._client.request_timeout
        params: list[str] = []
        for a in args:
            if isinstance(a, Message):
                if a.request_timeout is not None:
                    timeout = a.request_timeout
                params.append(json.dumps(_message_wire(a)))
            else:
                # to_wire encodes value types (bytes->base64, UUID/datetime/
                # Decimal/enum/dataclass/pydantic) and is a no-op for plain
                # JSON values, so existing primitive/dict args are unchanged.
                params.append(json.dumps(to_wire(a)))
        async def _connect_and_send() -> Response:
            # The dial shares the call deadline (see below) — a black-holed or
            # unreachable host no longer escapes `request_timeout` via an
            # unbounded connect. Mirrors .NET flowing one TimeoutHelper token
            # into connect+send.
            conn = await self._client._ensure_connected()
            # BeforeCall hook (client only — a reach-back proxy is bound to a
            # bare connection, which has no `before_call`, so callbacks skip it).
            before_call = getattr(self._client, "before_call", None)
            if before_call is not None:
                result = before_call(
                    CallInfo(self._endpoint_name, method_name, args)
                )
                if inspect.isawaitable(result):
                    await result
            req = Request(
                endpoint=self._endpoint_name,
                method_name=method_name,
                parameters=params,
                id=conn.next_id(),
                timeout_in_seconds=timeout,
            )
            return await conn.send_request(req)

        # Bound connect+send by the deadline. A non-positive timeout imposes no
        # client-side deadline: 0 ("use server default" — never an instant
        # wait_for(0)) and a negative value (.NET's Timeout.InfiniteTimeSpan,
        # -0.001 on the wire) both pass through unbounded; the server still
        # reads the wire value.
        if timeout is not None and timeout > 0:
            resp = await asyncio.wait_for(_connect_and_send(), timeout=timeout)
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
        # like .NET handing Newtonsoft `typeof(TResult)`. Plain dataclasses and
        # dict/Any/unannotated returns pass through as raw parsed structures so
        # consumers that decode results themselves (e.g. via from_wire) are
        # unaffected; pydantic models, enums, and scalar value types
        # (bytes/UUID/datetime/Decimal) — and containers of those — are built.
        return from_wire(
            parsed,
            _return_hint(self._contract, method_name),
            materialize_dataclasses=False,
        )
