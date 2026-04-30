from __future__ import annotations

import asyncio
import json
from typing import Any

from ..wire.messages import Error, Request, Response
from .cancellation import CancellationToken, CancellationTokenSource
from .contract import (
    ContractInfo,
    Message,
    OperationInfo,
    get_contract_info,
    is_cancellation_annotation,
    is_message_annotation,
)


class Router:
    """Endpoint → service instance lookup + argument binding + method dispatch.

    Codec-agnostic. Takes a decoded Request and returns a Response — the Connection
    layer is responsible for wire encoding.
    """

    def __init__(self) -> None:
        self._services: dict[str, tuple[ContractInfo, Any]] = {}

    def register(self, contract_cls: type, instance: Any) -> None:
        info = get_contract_info(contract_cls)
        self._services[info.name] = (info, instance)

    def has_endpoint(self, name: str) -> bool:
        return name in self._services

    async def dispatch(
        self,
        request: Request,
        cancel_event: asyncio.Event,
        callback_client: Any | None = None,
    ) -> Response:
        route = self._services.get(request.Endpoint)
        if route is None:
            return _error_response(
                request.Id,
                f"Endpoint '{request.Endpoint}' not found",
                "UiPath.Ipc.EndpointNotFoundException",
            )
        info, instance = route
        op = info.operations.get(request.MethodName)
        if op is None:
            return _error_response(
                request.Id,
                f"Method '{request.MethodName}' not found on '{request.Endpoint}'",
                "System.InvalidOperationException",
            )
        args = self._bind_args(op, request, cancel_event, callback_client)
        method = getattr(instance, op.name)
        try:
            result = await method(*args)
        except asyncio.CancelledError:
            return _error_response(
                request.Id,
                "A task was canceled.",
                "System.Threading.Tasks.TaskCanceledException",
            )
        except Exception as ex:
            return _error_response(
                request.Id,
                str(ex),
                type(ex).__module__ + "." + type(ex).__qualname__,
            )
        data = "" if op.return_type in (None, type(None)) else json.dumps(result)
        return Response(RequestId=request.Id, Data=data, Error=None)

    def _bind_args(
        self,
        op: OperationInfo,
        request: Request,
        cancel_event: asyncio.Event,
        callback_client: Any | None,
    ) -> list:
        args: list = []
        wire_iter = iter(request.Parameters)
        for pname, pann in op.params:
            if is_message_annotation(pann):
                message = Message()
                message.request_timeout = float(request.TimeoutInSeconds)
                message.client = callback_client
                args.append(message)
                continue
            if is_cancellation_annotation(pann):
                cts = CancellationTokenSource.from_event(cancel_event)
                args.append(cts.token)
                continue
            raw = next(wire_iter, None)
            if raw is None or raw == "":
                args.append(None)
                continue
            args.append(json.loads(raw))
        return args


def _error_response(request_id: str, message: str, type_name: str) -> Response:
    return Response(
        RequestId=request_id,
        Data=None,
        Error=Error(Message=message, StackTrace="", Type=type_name, InnerError=None),
    )
