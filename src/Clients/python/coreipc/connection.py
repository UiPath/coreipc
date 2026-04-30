from __future__ import annotations

import asyncio
import itertools
import logging
from typing import Awaitable, Callable

from .tracing import IpcTracer
from .wire.codec import Codec
from .wire.framing import DEFAULT_MAX_MESSAGE_SIZE, read_frame, write_frame
from .wire.messages import (
    CancellationRequest,
    Error,
    MessageType,
    Request,
    Response,
)

log = logging.getLogger("coreipc.connection")

RequestHandler = Callable[[Request, asyncio.Event], Awaitable[Response]]


class Connection:
    """One framed bidirectional stream.

    Owns request/response multiplexing by Request.Id, incoming request dispatch to a
    pluggable handler, and cancellation relay in both directions. Depends only on the
    Codec ABC + the (reader, writer) pair — unaware of transport or dispatch specifics.
    """

    def __init__(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        codec: Codec,
        *,
        request_handler: RequestHandler | None = None,
        max_message_size: int = DEFAULT_MAX_MESSAGE_SIZE,
        debug_name: str = "",
        tracer: IpcTracer | None = None,
    ) -> None:
        self._reader = reader
        self._writer = writer
        self._codec = codec
        self._request_handler = request_handler
        self._max_message_size = max_message_size
        self._debug_name = debug_name
        self._tracer = tracer
        self._send_lock = asyncio.Lock()
        self._pending: dict[str, asyncio.Future[Response]] = {}
        self._incoming: dict[str, tuple[asyncio.Task, asyncio.Event]] = {}
        self._id_counter = itertools.count(1)
        self._receive_task: asyncio.Task | None = None
        self._closed = asyncio.Event()

    def set_request_handler(self, handler: RequestHandler) -> None:
        self._request_handler = handler

    def next_request_id(self) -> str:
        return str(next(self._id_counter))

    def start(self) -> None:
        if self._receive_task is None:
            self._receive_task = asyncio.create_task(
                self._receive_loop(), name=f"coreipc-recv:{self._debug_name}"
            )

    async def remote_call(self, request: Request) -> Response:
        loop = asyncio.get_running_loop()
        future: asyncio.Future[Response] = loop.create_future()
        self._pending[request.Id] = future
        try:
            mt, payload = self._codec.encode_request(request)
            self._trace("on_call_sent", request)
            await self._send(mt, payload)
            try:
                response = await future
                self._trace("on_return_received", response)
                return response
            except asyncio.CancelledError:
                self._trace("on_cancel_sent", request.Id)
                await self._try_send_cancel(request.Id)
                raise
        finally:
            self._pending.pop(request.Id, None)

    async def close(self) -> None:
        if self._closed.is_set():
            return
        self._closed.set()
        for _, (task, _) in list(self._incoming.items()):
            task.cancel()
        for fut in list(self._pending.values()):
            if not fut.done():
                fut.set_exception(ConnectionResetError("Connection closed"))
        try:
            self._writer.close()
            await self._writer.wait_closed()
        except Exception:
            pass
        if self._receive_task is not None:
            self._receive_task.cancel()
            try:
                await self._receive_task
            except (asyncio.CancelledError, Exception):
                pass

    async def _send(self, mt: MessageType, payload: bytes) -> None:
        async with self._send_lock:
            await write_frame(self._writer, mt, payload, max_message_size=self._max_message_size)

    async def _try_send_cancel(self, request_id: str) -> None:
        try:
            mt, payload = self._codec.encode_cancel(CancellationRequest(RequestId=request_id))
            await self._send(mt, payload)
        except Exception:
            log.debug("Failed to send cancel for id=%s", request_id, exc_info=True)

    async def _receive_loop(self) -> None:
        try:
            while not self._closed.is_set():
                try:
                    mt, payload = await read_frame(
                        self._reader, max_message_size=self._max_message_size
                    )
                except asyncio.IncompleteReadError:
                    break
                message = self._codec.decode(mt, payload)
                if isinstance(message, Response):
                    fut = self._pending.get(message.RequestId)
                    if fut is not None and not fut.done():
                        fut.set_result(message)
                elif isinstance(message, Request):
                    self._trace("on_request_received", message)
                    self._spawn_handler(message)
                elif isinstance(message, CancellationRequest):
                    entry = self._incoming.get(message.RequestId)
                    if entry is not None:
                        task, cancel_event = entry
                        cancel_event.set()
                        task.cancel()
        except Exception as ex:
            log.warning("receive_loop error on %s: %r", self._debug_name, ex)
        finally:
            for fut in list(self._pending.values()):
                if not fut.done():
                    fut.set_exception(ConnectionResetError("Connection closed"))

    def _spawn_handler(self, request: Request) -> None:
        handler = self._request_handler
        if handler is None:
            response = Response(
                RequestId=request.Id,
                Data=None,
                Error=Error(
                    Message=f"No request handler registered (endpoint={request.Endpoint})",
                    StackTrace="",
                    Type="System.InvalidOperationException",
                    InnerError=None,
                ),
            )
            asyncio.create_task(self._send_response(response))
            return

        cancel_event = asyncio.Event()

        async def run() -> None:
            try:
                response = await handler(request, cancel_event)
            except asyncio.CancelledError:
                response = Response(
                    RequestId=request.Id,
                    Data=None,
                    Error=Error(
                        Message="A task was canceled.",
                        StackTrace="",
                        Type="System.Threading.Tasks.TaskCanceledException",
                        InnerError=None,
                    ),
                )
            except Exception as ex:
                response = Response(
                    RequestId=request.Id,
                    Data=None,
                    Error=Error(
                        Message=str(ex),
                        StackTrace="",
                        Type=type(ex).__module__ + "." + type(ex).__qualname__,
                        InnerError=None,
                    ),
                )
            finally:
                self._incoming.pop(request.Id, None)
            await self._send_response(response)

        task = asyncio.create_task(run(), name=f"coreipc-handle:{request.Id}")
        self._incoming[request.Id] = (task, cancel_event)

    async def _send_response(self, response: Response) -> None:
        try:
            mt, payload = self._codec.encode_response(response)
            await self._send(mt, payload)
            self._trace("on_response_sent", response)
        except Exception as ex:
            log.debug("Failed to send response for id=%s", response.RequestId, exc_info=True)
            self._trace("on_error", "_send_response", ex)

    def _trace(self, method: str, *args) -> None:
        if self._tracer is None:
            return
        try:
            getattr(self._tracer, method)(*args)
        except Exception:
            pass
