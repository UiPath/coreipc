from __future__ import annotations

import asyncio
from typing import Any

from .connection import Connection
from .dispatch.callback import CallbackClient
from .dispatch.proxy import build_proxy
from .dispatch.router import Router
from .tracing import IpcTracer
from .transport.base import ClientTransport
from .wire.codec import Codec
from .wire.codec_coreipc import CoreIpcCodec


class IpcClient:
    """Builder + lazy connect. get_proxy returns a proxy bound to the single managed connection.

    Registered callbacks turn the connection bidirectional: when the server invokes a method
    on a registered contract, the local Router dispatches it on the callback instance.
    """

    def __init__(self) -> None:
        self._transport: ClientTransport | None = None
        self._codec: Codec = CoreIpcCodec()
        self._callback_router = Router()
        self._tracer: IpcTracer | None = None
        self._connection: Connection | None = None
        self._connect_lock = asyncio.Lock()

    def with_transport(self, transport: ClientTransport) -> "IpcClient":
        self._transport = transport
        return self

    def with_codec(self, codec: Codec) -> "IpcClient":
        self._codec = codec
        return self

    def with_callback(self, contract_cls: type, instance: Any) -> "IpcClient":
        self._callback_router.register(contract_cls, instance)
        return self

    def with_tracer(self, tracer: IpcTracer) -> "IpcClient":
        self._tracer = tracer
        return self

    async def connect(self) -> Connection:
        if self._connection is not None and not self._connection._closed.is_set():
            return self._connection
        async with self._connect_lock:
            if self._connection is not None and not self._connection._closed.is_set():
                return self._connection
            if self._transport is None:
                raise RuntimeError(
                    "IpcClient: no transport configured (call with_transport)."
                )
            reader, writer = await self._transport.connect()
            conn = Connection(
                reader, writer, self._codec, debug_name="client", tracer=self._tracer
            )
            callback_client = CallbackClient(conn)

            async def handle(request, cancel_event):
                return await self._callback_router.dispatch(
                    request, cancel_event, callback_client
                )

            conn.set_request_handler(handle)
            conn.start()
            self._connection = conn
            return conn

    def get_proxy(self, contract_cls: type) -> Any:
        return build_proxy(contract_cls, _LazyChannel(self))

    async def close(self) -> None:
        if self._connection is not None:
            await self._connection.close()
            self._connection = None


class _LazyChannel:
    """Channel adapter that connects on first use, delegates to Connection afterwards."""

    def __init__(self, client: IpcClient) -> None:
        self._client = client

    def next_request_id(self) -> str:
        conn = self._client._connection
        if conn is None:
            # Ids only matter post-connect; return a placeholder — _BoundOperation awaits the
            # real call path, which calls remote_call below where connection is ensured.
            return "0"
        return conn.next_request_id()

    async def remote_call(self, request):  # type: ignore[no-untyped-def]
        conn = await self._client.connect()
        # Re-stamp Id now that the connection exists (placeholder "0" otherwise collides).
        request.Id = conn.next_request_id()
        return await conn.remote_call(request)
