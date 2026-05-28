"""Dynamic proxy that turns Python method calls into IPC requests."""

from __future__ import annotations

import inspect
import json
from typing import TYPE_CHECKING, Any

from ..errors import RemoteException
from ..wire import Request

if TYPE_CHECKING:
    from .ipc_client import IpcClient


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
        params = [json.dumps(a) for a in args]
        conn = await self._client._ensure_connected()
        req = Request(
            endpoint=self._endpoint_name,
            method_name=method_name,
            parameters=params,
            id=conn.next_id(),
        )
        resp = await conn.send_request(req)
        if resp.error is not None:
            raise RemoteException(resp.error)
        if resp.data is None:
            return None
        return json.loads(resp.data)
