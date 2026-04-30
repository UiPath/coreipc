from __future__ import annotations

import asyncio
import logging
from typing import Any

from .connection import Connection
from .dispatch.callback import CallbackClient
from .dispatch.router import Router
from .tracing import IpcTracer
from .transport.base import ServerHandle, ServerTransport
from .wire.codec import Codec
from .wire.codec_coreipc import CoreIpcCodec

log = logging.getLogger("coreipc.server")


class IpcServer:
    """Builder + accept loop. Creates one Connection per incoming client."""

    def __init__(self) -> None:
        self._transport: ServerTransport | None = None
        self._codec: Codec = CoreIpcCodec()
        self._router = Router()
        self._tracer: IpcTracer | None = None
        self._handle: ServerHandle | None = None
        self._connections: set[Connection] = set()

    def with_transport(self, transport: ServerTransport) -> "IpcServer":
        self._transport = transport
        return self

    def with_codec(self, codec: Codec) -> "IpcServer":
        self._codec = codec
        return self

    def with_service(self, contract_cls: type, instance: Any) -> "IpcServer":
        self._router.register(contract_cls, instance)
        return self

    def with_tracer(self, tracer: IpcTracer) -> "IpcServer":
        self._tracer = tracer
        return self

    async def start(self) -> None:
        if self._transport is None:
            raise RuntimeError("IpcServer: no transport configured (call with_transport).")

        async def on_connection(
            reader: asyncio.StreamReader, writer: asyncio.StreamWriter
        ) -> None:
            conn = Connection(
                reader, writer, self._codec, debug_name="server", tracer=self._tracer
            )
            callback_client = CallbackClient(conn)

            async def handle(request, cancel_event):
                return await self._router.dispatch(request, cancel_event, callback_client)

            conn.set_request_handler(handle)
            self._connections.add(conn)
            try:
                conn.start()
                await conn._closed.wait()
            finally:
                self._connections.discard(conn)

        self._handle = await self._transport.serve(on_connection)

    async def stop(self) -> None:
        for conn in list(self._connections):
            await conn.close()
        if self._handle is not None:
            await self._handle.close()
            await self._handle.wait_closed()
