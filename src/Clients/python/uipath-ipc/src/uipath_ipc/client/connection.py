"""Single duplex connection between client and server.

Owns:
  - the (StreamReader, StreamWriter) pair from a `ClientTransport`,
  - a background receive-loop that decodes frames,
  - a map of pending requests keyed by Request.id.

`send_request(req)` sends and awaits the matching Response. The connection
auto-generates IDs (`next_id`).
"""

from __future__ import annotations

import asyncio
import itertools

from ..transport.base import ClientTransport
from ..wire import (
    CancellationRequest,
    MessageType,
    Request,
    Response,
    read_frame,
    write_frame,
)


class IpcConnection:
    """One duplex stream + the request/response dispatcher around it."""

    def __init__(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
    ) -> None:
        self._reader = reader
        self._writer = writer
        self._pending: dict[str, asyncio.Future[Response]] = {}
        self._id_counter = itertools.count(1)
        self._receive_task: asyncio.Task[None] | None = None
        self._closed = False

    # --- lifecycle ---------------------------------------------------------

    @classmethod
    async def open(cls, transport: ClientTransport) -> IpcConnection:
        """Connect via the transport, wrap the stream in a new connection."""
        reader, writer = await transport.connect()
        conn = cls(reader, writer)
        conn.start()
        return conn

    def start(self) -> None:
        """Begin the receive loop. Idempotent."""
        if self._receive_task is not None:
            return
        self._receive_task = asyncio.create_task(self._receive_loop())

    async def aclose(self) -> None:
        """Close the connection and fail any in-flight requests."""
        if self._closed:
            return
        self._closed = True
        if self._receive_task is not None:
            self._receive_task.cancel()
        try:
            self._writer.close()
            await self._writer.wait_closed()
        except Exception:
            pass
        self._fail_pending(ConnectionError("connection closed"))

    async def __aenter__(self) -> IpcConnection:
        return self

    async def __aexit__(self, *exc_info: object) -> None:
        await self.aclose()

    # --- public API --------------------------------------------------------

    @property
    def is_closed(self) -> bool:
        return self._closed

    def next_id(self) -> str:
        return str(next(self._id_counter))

    async def send_request(self, req: Request) -> Response:
        """Send a request and await the matching response.

        If the awaiting task is cancelled, a best-effort
        `CancellationRequest` is sent to the server with the matching id,
        and `CancelledError` is re-raised so the cancellation propagates.
        """
        if self._closed:
            raise ConnectionError("connection is closed")

        loop = asyncio.get_running_loop()
        fut: asyncio.Future[Response] = loop.create_future()
        self._pending[req.id] = fut
        try:
            payload = req.to_json().encode("utf-8")
            await write_frame(self._writer, MessageType.REQUEST, payload)
            return await fut
        except asyncio.CancelledError:
            # Fire-and-forget — the awaiting task is being torn down, but
            # the cancellation message can still go out on the writer.
            asyncio.create_task(self._safe_send_cancellation(req.id))
            raise
        finally:
            self._pending.pop(req.id, None)

    async def _safe_send_cancellation(self, request_id: str) -> None:
        """Best-effort: send a CancellationRequest, swallow any errors."""
        if self._closed:
            return
        try:
            payload = (
                CancellationRequest(request_id=request_id)
                .to_json()
                .encode("utf-8")
            )
            await write_frame(self._writer, MessageType.CANCELLATION_REQUEST, payload)
        except Exception:
            pass

    # --- receive loop ------------------------------------------------------

    async def _receive_loop(self) -> None:
        try:
            while not self._closed:
                msg_type, payload = await read_frame(self._reader)
                if msg_type == MessageType.RESPONSE:
                    self._handle_response(payload)
                # Other message types (cancellation echoes, upload/download)
                # are not expected on the client receive path right now.
        except asyncio.CancelledError:
            raise
        except (asyncio.IncompleteReadError, ConnectionResetError, OSError) as ex:
            self._fail_pending(ex)
        except Exception as ex:  # noqa: BLE001 — surface anything unexpected via futures
            self._fail_pending(ex)

    def _handle_response(self, payload: bytes) -> None:
        resp = Response.from_json(payload.decode("utf-8"))
        fut = self._pending.get(resp.request_id)
        if fut is not None and not fut.done():
            fut.set_result(resp)

    def _fail_pending(self, ex: BaseException) -> None:
        for fut in list(self._pending.values()):
            if not fut.done():
                fut.set_exception(ex)
        self._pending.clear()
