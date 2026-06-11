"""Dynamic proxy that turns Python method calls into IPC requests."""

from __future__ import annotations

import asyncio
import inspect
import json
from typing import TYPE_CHECKING, Any

from ..errors import RemoteException
from ..hooks import CallInfo
from ..message import Message
from ..wire import Request

if TYPE_CHECKING:
    from .ipc_client import IpcClient


def _message_wire(m: Message) -> dict:
    """The wire form of a `Message` argument, matching .NET: a payload-less
    `Message` serializes to `{}`; `Message[T]` to `{"Payload": <payload>}`.
    `client`/`request_timeout` are transport-only (never serialized)."""
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
                params.append(json.dumps(a))
        conn = await self._client._ensure_connected()
        # BeforeCall hook (client only — a reach-back proxy is bound to a bare
        # connection, which has no `before_call`, so callbacks skip it).
        before_call = getattr(self._client, "before_call", None)
        if before_call is not None:
            result = before_call(CallInfo(self._endpoint_name, method_name, args))
            if inspect.isawaitable(result):
                await result
        req = Request(
            endpoint=self._endpoint_name,
            method_name=method_name,
            parameters=params,
            id=conn.next_id(),
            timeout_in_seconds=timeout,
        )
        if timeout is not None:
            resp = await asyncio.wait_for(conn.send_request(req), timeout=timeout)
        else:
            resp = await conn.send_request(req)
        if resp.error is not None:
            raise RemoteException.from_error(resp.error)
        # Void / fire-and-forget operations answer with an empty Data string
        # (not null) — e.g. .NET CoreIpc's response for a `Task`-returning
        # method. Treat empty (or null) Data as "no return value".
        if not resp.data:
            return None
        return json.loads(resp.data)
