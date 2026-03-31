"""ServiceClient: manages connection lifecycle and the invoke pipeline.

Mirrors ServiceClientProper from the .NET implementation.
"""

from __future__ import annotations

import asyncio
import json
import logging
from typing import Any

from ..connection import Connection
from ..errors import RemoteException
from ..transport.base import ClientTransport
from ..wire.dtos import Request, Response
from ..wire.serializer import serialize_parameter, deserialize_parameter
from .proxy import IpcProxy

logger = logging.getLogger(__name__)


class ServiceClient:
    """Manages a lazy connection to a server and provides the invoke pipeline.

    Creates an IpcProxy for the given interface type. On first method call,
    establishes the connection. Reuses the connection for subsequent calls.
    """

    def __init__(
        self,
        transport: ClientTransport,
        interface_type: type,
        request_timeout: float | None = None,
        debug_name: str | None = None,
    ) -> None:
        self._transport = transport
        self._interface_type = interface_type
        self._request_timeout = request_timeout
        self._debug_name = debug_name or f"Client<{interface_type.__name__}>"

        self._connection: Connection | None = None
        self._connect_lock = asyncio.Lock()
        self._listen_task: asyncio.Task[None] | None = None
        self._proxy = IpcProxy(self, interface_type)

    @property
    def proxy(self) -> Any:
        return self._proxy

    async def ensure_connection(self) -> Connection:
        async with self._connect_lock:
            if self._connection is not None and not self._connection.is_closed:
                return self._connection

            reader, writer = await self._transport.connect()
            self._connection = Connection(
                reader, writer, debug_name=self._debug_name
            )
            self._listen_task = asyncio.create_task(self._connection.listen())
            return self._connection

    async def invoke(self, method_name: str, args: tuple[Any, ...], return_type: type | None) -> Any:
        """Serialize arguments, send request, wait for response, deserialize result."""
        connection = await self.ensure_connection()

        serialized_args = [serialize_parameter(arg) for arg in args]

        request_id = connection.new_request_id()
        request = Request(
            Endpoint=self._interface_type.__name__,
            Id=request_id,
            MethodName=method_name,
            Parameters=serialized_args,
            TimeoutInSeconds=self._request_timeout or 0.0,
        )

        response = await connection.remote_call(request)

        if response.Error:
            raise RemoteException(response.Error)

        if response.Data is None or response.Data == "":
            return None

        return deserialize_parameter(response.Data, return_type)

    async def close(self) -> None:
        """Close the underlying connection."""
        async with self._connect_lock:
            if self._connection:
                self._connection.close()
                self._connection = None
            if self._listen_task:
                self._listen_task.cancel()
                try:
                    await self._listen_task
                except (asyncio.CancelledError, Exception):
                    pass
                self._listen_task = None
