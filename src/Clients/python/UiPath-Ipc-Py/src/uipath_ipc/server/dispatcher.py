"""RPC dispatcher: receives Request, resolves endpoint, invokes method, sends Response.

Mirrors Server.cs from the .NET implementation.
"""

from __future__ import annotations

import asyncio
import inspect
import json
import logging
from typing import Any, get_type_hints

from ..cancellation import CancellationToken
from ..connection import Connection
from ..wire.dtos import Error, Request, Response
from ..wire.serializer import deserialize_parameter, serialize_parameter
from .router import Router

logger = logging.getLogger(__name__)


class Dispatcher:
    """Server-side RPC dispatcher.

    Wires up to a Connection's on_request/on_cancellation callbacks.
    On each incoming request: resolves the endpoint, finds the method,
    deserializes arguments, invokes the method, serializes the response.
    """

    def __init__(
        self,
        router: Router,
        request_timeout: float | None,
        connection: Connection,
    ) -> None:
        self._router = router
        self._request_timeout = request_timeout
        self._connection = connection
        self._pending_cancellations: dict[str, CancellationToken] = {}

        connection.on_request = self._on_request_received
        connection.on_cancellation = self._cancel_request

    def _cancel_request(self, request_id: str) -> None:
        token = self._pending_cancellations.pop(request_id, None)
        if token:
            token.cancel()

    async def _on_request_received(self, request: Request) -> None:
        try:
            settings = self._router.resolve(request.Endpoint)
            service = settings.get_service()
            method = getattr(service, request.MethodName, None)
            if method is None:
                raise AttributeError(
                    f"Method '{request.MethodName}' not found on {type(service).__name__}."
                )

            # Set up per-request cancellation
            cancel_token = CancellationToken()
            self._pending_cancellations[request.Id] = cancel_token

            try:
                # Before incoming call hook
                if settings.before_incoming_call:
                    await _maybe_await(settings.before_incoming_call(method, cancel_token))

                # Deserialize arguments
                args = self._deserialize_arguments(method, request, cancel_token)

                # Invoke
                result = await method(*args)

                # Serialize response
                if result is None:
                    data = ""
                else:
                    data = serialize_parameter(result)

                response = Response.success(request, data)
            finally:
                self._pending_cancellations.pop(request.Id, None)

            await self._connection.send_response(response)

        except Exception as ex:
            logger.debug("Error processing request %s: %s", request, ex)
            try:
                response = Response.fail(request, ex)
                await self._connection.send_response(response)
            except Exception as send_ex:
                logger.error("Failed to send error response: %s", send_ex)

    def _deserialize_arguments(
        self,
        method: Any,
        request: Request,
        cancel_token: CancellationToken,
    ) -> list[Any]:
        """Deserialize request parameters based on method signature type hints."""
        sig = inspect.signature(method)
        hints = get_type_hints(method)
        params = list(sig.parameters.values())

        # Skip 'self' parameter if present (bound methods won't have it, but be safe)
        if params and params[0].name == "self":
            params = params[1:]

        args: list[Any] = []
        request_params = request.Parameters

        for i, param in enumerate(params):
            param_type = hints.get(param.name)

            # CancellationToken parameter -> inject the request's token
            if param_type is CancellationToken:
                args.append(cancel_token)
                continue

            # If we have a value from the request
            if i < len(request_params):
                raw = request_params[i]
                if not raw and param_type is CancellationToken:
                    args.append(cancel_token)
                elif not raw:
                    # Empty string for CancellationToken slots from .NET
                    args.append(param.default if param.default is not inspect.Parameter.empty else None)
                else:
                    args.append(deserialize_parameter(raw, param_type))
            elif param.default is not inspect.Parameter.empty:
                args.append(param.default)
            else:
                args.append(None)

        return args


async def _maybe_await(result: Any) -> None:
    """Await the result if it's a coroutine."""
    if asyncio.iscoroutine(result) or asyncio.isfuture(result):
        await result
