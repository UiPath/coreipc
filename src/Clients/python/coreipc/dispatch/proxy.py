from __future__ import annotations

import asyncio
import json
from typing import Any

from ..wire.messages import Request
from .cancellation import CancellationToken
from .contract import (
    ContractInfo,
    Message,
    OperationInfo,
    get_contract_info,
    is_cancellation_annotation,
    is_message_annotation,
)
from .errors import classify


class _ProxyChannel:
    """Minimal surface the proxy needs from Connection. Keeps the proxy unit-testable."""

    def next_request_id(self) -> str: ...  # pragma: no cover
    async def remote_call(self, request: Request) -> Any: ...  # pragma: no cover


class _BoundOperation:
    __slots__ = ("_contract_name", "_op", "_channel")

    def __init__(self, contract_name: str, op: OperationInfo, channel: _ProxyChannel) -> None:
        self._contract_name = contract_name
        self._op = op
        self._channel = channel

    async def __call__(self, *args: Any, **kwargs: Any) -> Any:
        wire_args, ct = self._collect_args(args, kwargs)
        request = Request(
            Endpoint=self._contract_name,
            Id=self._channel.next_request_id(),
            MethodName=self._op.name,
            Parameters=[json.dumps(a) for a in wire_args],
            TimeoutInSeconds=float(self._op.timeout_seconds or 0.0),
        )
        call_task = asyncio.create_task(
            self._channel.remote_call(request), name=f"coreipc-call:{request.Id}"
        )
        unsubscribe = None
        if ct is not None:
            unsubscribe = ct.register(call_task.cancel)
        try:
            response = await call_task
        finally:
            if unsubscribe is not None:
                unsubscribe()
        if response.Error is not None:
            raise classify(response.Error)
        if response.Data is None or self._op.return_type in (None, type(None)):
            return None
        return json.loads(response.Data)

    def _collect_args(self, args: tuple, kwargs: dict) -> tuple[list, CancellationToken | None]:
        wire_args: list = []
        ct: CancellationToken | None = None
        arg_iter = iter(args)
        for pname, pann in self._op.params:
            if is_message_annotation(pann):
                # Not sent on wire (v1: plain Message with no payload).
                continue
            if is_cancellation_annotation(pann):
                if pname in kwargs:
                    ct = kwargs.pop(pname)
                else:
                    ct = next(arg_iter, None)
                continue
            if pname in kwargs:
                wire_args.append(kwargs.pop(pname))
            else:
                try:
                    wire_args.append(next(arg_iter))
                except StopIteration:
                    wire_args.append(None)
        if kwargs:
            raise TypeError(f"Unexpected keyword args for {self._op.name}: {list(kwargs)}")
        return wire_args, ct


class IpcProxy:
    """Holder that resolves remote operations via __getattr__ (no dynamic subclassing)."""

    def __init__(self, contract_info: ContractInfo, channel: _ProxyChannel) -> None:
        self._info = contract_info
        self._channel = channel

    def __getattr__(self, name: str) -> _BoundOperation:
        try:
            info = object.__getattribute__(self, "_info")
        except AttributeError as exc:
            raise AttributeError(name) from exc
        op = info.operations.get(name)
        if op is None:
            raise AttributeError(f"{info.name} has no @operation named {name!r}")
        channel = object.__getattribute__(self, "_channel")
        return _BoundOperation(info.name, op, channel)


def build_proxy(contract_cls: type, channel: _ProxyChannel) -> IpcProxy:
    return IpcProxy(get_contract_info(contract_cls), channel)
