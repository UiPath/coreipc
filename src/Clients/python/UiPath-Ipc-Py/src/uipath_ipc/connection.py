"""Bidirectional message I/O over a stream, mirroring Connection.cs."""

from __future__ import annotations

import asyncio
import logging
from typing import Awaitable, Callable

from .wire.dtos import (
    CancellationRequest,
    MessageType,
    Request,
    Response,
)
from .wire.framing import read_message, write_message
from .wire.serializer import deserialize_message, serialize_message

logger = logging.getLogger(__name__)


class Connection:
    """Manages bidirectional IPC message I/O over an asyncio stream pair.

    Mirrors the .NET Connection class: receive loop, request correlation,
    send lock, and monotonic request IDs.
    """

    def __init__(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        debug_name: str = "",
        max_message_size: int = 2 * 1024 * 1024,
    ) -> None:
        self._reader = reader
        self._writer = writer
        self.debug_name = debug_name
        self._max_message_size = max_message_size

        self._request_counter = -1
        self._pending: dict[str, asyncio.Future[Response]] = {}
        self._send_lock = asyncio.Lock()
        self._closed = False

        # Callbacks set by Server dispatcher or ServiceClient
        self.on_request: Callable[[Request], Awaitable[None]] | None = None
        self.on_cancellation: Callable[[str], None] | None = None
        self.on_closed: Callable[[], None] | None = None

    @property
    def is_closed(self) -> bool:
        return self._closed

    def new_request_id(self) -> str:
        self._request_counter += 1
        return str(self._request_counter)

    async def listen(self) -> None:
        """Run the receive loop until the connection closes."""
        try:
            while True:
                msg = await read_message(self._reader)
                if msg is None:
                    break

                msg_type, payload = msg

                if len(payload) > self._max_message_size:
                    logger.error(
                        "Message too large (%d bytes). Max is %d.",
                        len(payload),
                        self._max_message_size,
                    )
                    break

                await self._handle_message(msg_type, payload)
        except Exception as ex:
            logger.debug("Receive loop failed for %s: %s", self.debug_name, ex)
        finally:
            self._close()

    async def remote_call(self, request: Request) -> Response:
        """Send a request and wait for the correlated response."""
        loop = asyncio.get_running_loop()
        future: asyncio.Future[Response] = loop.create_future()
        request_id = request.Id
        self._pending[request_id] = future

        try:
            await self._send_request(request)
        except Exception:
            self._pending.pop(request_id, None)
            raise

        try:
            return await future
        finally:
            self._pending.pop(request_id, None)

    async def send_response(self, response: Response) -> None:
        """Send a response message (used by the server dispatcher)."""
        payload = serialize_message(response)
        async with self._send_lock:
            await write_message(self._writer, MessageType.Response, payload)

    async def send_cancellation(self, request_id: str) -> None:
        """Send a cancellation request for a pending request."""
        cancel_msg = CancellationRequest(RequestId=request_id)
        payload = serialize_message(cancel_msg)
        async with self._send_lock:
            await write_message(self._writer, MessageType.CancellationRequest, payload)

    def cancel_pending(self, request_id: str) -> None:
        """Cancel a pending request locally (and send cancellation to remote)."""
        future = self._pending.pop(request_id, None)
        if future and not future.done():
            future.cancel()
        asyncio.ensure_future(self.send_cancellation(request_id))

    def close(self) -> None:
        """Close the connection."""
        self._close()

    # -- Internal --

    async def _send_request(self, request: Request) -> None:
        payload = serialize_message(request)
        async with self._send_lock:
            await write_message(self._writer, MessageType.Request, payload)

    async def _handle_message(self, msg_type: MessageType, payload: bytes) -> None:
        if msg_type == MessageType.Response:
            response = deserialize_message(payload, Response)
            self._on_response_received(response)
        elif msg_type == MessageType.Request:
            request = deserialize_message(payload, Request)
            await self._on_request_received(request)
        elif msg_type == MessageType.CancellationRequest:
            cancel = deserialize_message(payload, CancellationRequest)
            self._on_cancellation_received(cancel)
        else:
            logger.warning("Unknown message type: %s", msg_type)

    def _on_response_received(self, response: Response) -> None:
        future = self._pending.pop(response.RequestId, None)
        if future and not future.done():
            future.set_result(response)

    async def _on_request_received(self, request: Request) -> None:
        if self.on_request:
            try:
                await self.on_request(request)
            except Exception as ex:
                logger.error("Error handling request %s: %s", request, ex)

    def _on_cancellation_received(self, cancel: CancellationRequest) -> None:
        if self.on_cancellation:
            try:
                self.on_cancellation(cancel.RequestId)
            except Exception as ex:
                logger.error("Error handling cancellation %s: %s", cancel.RequestId, ex)

    def _close(self) -> None:
        if self._closed:
            return
        self._closed = True

        try:
            self._writer.close()
        except Exception:
            pass

        # Fail all pending requests
        closed_error = ConnectionError("Connection closed.")
        for request_id in list(self._pending.keys()):
            future = self._pending.pop(request_id, None)
            if future and not future.done():
                future.set_exception(closed_error)

        if self.on_closed:
            try:
                self.on_closed()
            except Exception as ex:
                logger.error("Error in on_closed handler: %s", ex)
