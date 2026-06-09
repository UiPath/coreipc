"""Single duplex connection between client and server.

Owns:
  - the (StreamReader, StreamWriter) pair from a `ClientTransport`,
  - a background receive-loop that decodes frames,
  - a map of pending OUTGOING requests keyed by Request.id (awaited by
    `send_request`),
  - a map of in-flight INCOMING request handler tasks (so we can cancel
    them when the server sends a CancellationRequest),
  - a single write lock so multiple producers (outgoing requests,
    callback responses, cancellation messages) can share the writer
    without interleaving bytes.

Outgoing path:
  `send_request(req)` writes a Request frame and awaits the matching
  Response. Caller cancellation triggers a best-effort CancellationRequest.

Incoming path (callbacks):
  The .NET server can call into the Python client. Pass
  `callbacks={endpoint_name: instance}` (or via `IpcClient(callbacks=...)`).
  An incoming Request frame is dispatched to `instance.<method_name>(*args)`;
  the result is encoded into a Response frame. Exceptions become Error
  responses. Server cancellations cancel the handler task.
"""

from __future__ import annotations

import asyncio
import inspect
import itertools
import json
import traceback
import weakref
from typing import Callable, TypeVar, cast, get_origin, get_type_hints

from ..message import Message
from ..transport.base import ClientTransport
from ..wire import (
    CancellationRequest,
    Error,
    MessageType,
    Request,
    Response,
    read_frame,
    write_frame,
)

T = TypeVar("T")

#: Invoked once with the connection when it closes (e.g. to prune it from a
#: server's live-connection set). Should be synchronous and must not raise.
CloseCallback = Callable[["IpcConnection"], object]


def _is_message_annotation(annotation: object) -> bool:
    """True if a parameter annotation refers to `Message` or `Message[T]`."""
    if annotation is Message:
        return True
    if isinstance(annotation, str):
        # `from __future__ import annotations` leaves annotations as strings
        # when get_type_hints can't resolve them; match by spelling.
        return annotation == "Message" or annotation.startswith("Message[")
    return get_origin(annotation) is Message


# A handler's argument-binding plan: one tag per parameter (self excluded).
#   "wire"    -> take the next positional wire argument
#   "message" -> inject a Message (consumes no wire argument)
#   "varargs" -> *args: absorb all remaining wire arguments
#   "skip"    -> **kwargs / keyword-only: not fillable from positional wire
# Cached weakly by the underlying function so it's computed once per method.
_binding_plan_cache: "weakref.WeakKeyDictionary[object, tuple[str, ...]]" = (
    weakref.WeakKeyDictionary()
)


def _binding_plan(method: Callable[..., object]) -> tuple[str, ...]:
    """Compute (and cache) how to map wire args onto a handler's parameters."""
    func = getattr(method, "__func__", method)
    cached = _binding_plan_cache.get(func)
    if cached is not None:
        return cached

    try:
        hints = get_type_hints(func)
    except Exception:
        hints = {}
    plan: list[str] = []
    for name, param in inspect.signature(method).parameters.items():
        if param.kind is inspect.Parameter.VAR_POSITIONAL:
            plan.append("varargs")
        elif param.kind in (
            inspect.Parameter.VAR_KEYWORD,
            inspect.Parameter.KEYWORD_ONLY,
        ):
            plan.append("skip")
        elif _is_message_annotation(hints.get(name, param.annotation)):
            plan.append("message")
        else:
            plan.append("wire")
    result = tuple(plan)
    try:
        _binding_plan_cache[func] = result
    except TypeError:
        pass  # builtins / unweakreferenceable callables: just don't cache
    return result


class _ConnectionInvoker:
    """Adapts one open `IpcConnection` to the minimal surface `_IpcProxy`
    needs — an already-connected `_ensure_connected` plus a `request_timeout`
    — so reach-back proxies can be built without an owning `IpcClient`."""

    __slots__ = ("_connection", "request_timeout")

    def __init__(
        self, connection: IpcConnection, request_timeout: float | None
    ) -> None:
        self._connection = connection
        self.request_timeout = request_timeout

    async def _ensure_connected(self) -> IpcConnection:
        return self._connection


class IpcConnection:
    """One duplex stream + the bidirectional request/response dispatcher."""

    def __init__(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        callbacks: dict[str, object] | None = None,
        request_timeout: float | None = None,
    ) -> None:
        self._reader = reader
        self._writer = writer
        self._callbacks: dict[str, object] = dict(callbacks or {})
        #: Default timeout for reach-back proxies built via `get_callback`.
        self.request_timeout = request_timeout
        self._pending: dict[str, asyncio.Future[Response]] = {}
        self._incoming_handlers: dict[str, asyncio.Task[None]] = {}
        self._id_counter = itertools.count(1)
        self._receive_task: asyncio.Task[None] | None = None
        self._write_lock = asyncio.Lock()
        self._closed = False
        self._close_callbacks: list[CloseCallback] = []
        self._close_notified = False

    # --- lifecycle ---------------------------------------------------------

    @classmethod
    async def open(
        cls,
        transport: ClientTransport,
        callbacks: dict[str, object] | None = None,
        request_timeout: float | None = None,
    ) -> IpcConnection:
        """Connect via the transport, wrap the stream in a new connection."""
        reader, writer = await transport.connect()
        conn = cls(
            reader, writer, callbacks=callbacks, request_timeout=request_timeout
        )
        conn.start()
        return conn

    def start(self) -> None:
        """Begin the receive loop. Idempotent."""
        if self._receive_task is not None:
            return
        self._receive_task = asyncio.create_task(self._receive_loop())

    async def aclose(self) -> None:
        """Close the connection and fail/cancel any in-flight work."""
        if self._closed:
            return
        self._closed = True
        if self._receive_task is not None:
            self._receive_task.cancel()
        # Cancel in-flight callback handlers so they don't outlive the stream.
        for task in list(self._incoming_handlers.values()):
            task.cancel()
        self._incoming_handlers.clear()
        try:
            self._writer.close()
            await self._writer.wait_closed()
        except Exception:
            pass
        self._fail_pending(ConnectionError("connection closed"))
        self._notify_closed()

    async def __aenter__(self) -> IpcConnection:
        return self

    async def __aexit__(self, *exc_info: object) -> None:
        await self.aclose()

    # --- public API --------------------------------------------------------

    @property
    def is_closed(self) -> bool:
        return self._closed

    def add_close_callback(self, callback: CloseCallback) -> None:
        """Register a callback invoked exactly once when this connection closes.

        The callback receives this connection. It fires from whichever path
        closes the connection first — an explicit `aclose()` or the receive
        loop ending (peer disconnect / I/O error). If the connection is
        already closed, the callback runs immediately. Used by `IpcServer`
        to prune connections from its live set. Callbacks should be
        synchronous and must not raise.
        """
        if self._close_notified:
            callback(self)
            return
        self._close_callbacks.append(callback)

    def _notify_closed(self) -> None:
        """Fire close callbacks once, swallowing any errors they raise."""
        if self._close_notified:
            return
        self._close_notified = True
        for cb in self._close_callbacks:
            try:
                cb(self)
            except Exception:
                pass
        self._close_callbacks.clear()

    def next_id(self) -> str:
        return str(next(self._id_counter))

    def get_callback(self, contract: type[T]) -> T:
        """Return a proxy that calls `contract` back over THIS connection.

        The inverse direction of an in-flight request: a handler invoked on
        this connection can call methods the *peer* hosts (its registered
        callbacks/services). Mirrors .NET's ``IClient.GetCallback<T>()``.
        Usually reached via an injected `Message`: ``m.client.get_callback``.
        """
        from .proxy import _IpcProxy  # local import avoids an import cycle

        invoker = _ConnectionInvoker(self, self.request_timeout)
        return cast(T, _IpcProxy(invoker, contract))

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
            await self._send_frame(MessageType.REQUEST, payload)
            return await fut
        except asyncio.CancelledError:
            asyncio.create_task(self._safe_send_cancellation(req.id))
            raise
        finally:
            self._pending.pop(req.id, None)

    # --- frame I/O ---------------------------------------------------------

    async def _send_frame(self, msg_type: MessageType, payload: bytes) -> None:
        """Write one frame atomically under the write lock."""
        async with self._write_lock:
            await write_frame(self._writer, msg_type, payload)

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
            await self._send_frame(
                MessageType.CANCELLATION_REQUEST, payload
            )
        except Exception:
            pass

    # --- receive loop ------------------------------------------------------

    async def _receive_loop(self) -> None:
        try:
            while not self._closed:
                msg_type, payload = await read_frame(self._reader)
                if msg_type == MessageType.RESPONSE:
                    self._handle_response(payload)
                elif msg_type == MessageType.REQUEST:
                    self._handle_incoming_request(payload)
                elif msg_type == MessageType.CANCELLATION_REQUEST:
                    self._handle_incoming_cancellation(payload)
                # UPLOAD_REQUEST / DOWNLOAD_RESPONSE are not yet handled.
        except asyncio.CancelledError:
            raise
        except (asyncio.IncompleteReadError, ConnectionResetError, OSError) as ex:
            self._fail_pending(ex)
        except Exception as ex:  # noqa: BLE001 — surface anything unexpected via futures
            self._fail_pending(ex)
        finally:
            # Mark closed so the owning IpcClient knows to re-dial on next call.
            self._closed = True
            # Tear down our own writer so its transport doesn't leak. On peer
            # disconnect the connection is pruned from any owning IpcServer, so
            # aclose() won't run for it — this is the only cleanup it gets.
            try:
                self._writer.close()
            except Exception:
                pass
            self._fail_pending(ConnectionError("connection closed"))
            # Notify owners (e.g. IpcServer) so they can prune this connection.
            self._notify_closed()

    def _handle_response(self, payload: bytes) -> None:
        resp = Response.from_json(payload.decode("utf-8"))
        fut = self._pending.get(resp.request_id)
        if fut is not None and not fut.done():
            fut.set_result(resp)

    def _handle_incoming_request(self, payload: bytes) -> None:
        """Dispatch an incoming Request to a registered callback in a task.

        Runs in a background task so the receive loop stays free for the
        next frame.
        """
        req = Request.from_json(payload.decode("utf-8"))
        task = asyncio.create_task(self._invoke_callback(req))
        self._incoming_handlers[req.id] = task
        task.add_done_callback(
            lambda _t, rid=req.id: self._incoming_handlers.pop(rid, None)
        )

    def _handle_incoming_cancellation(self, payload: bytes) -> None:
        """Cancel an in-flight incoming-request handler by id."""
        cancel = CancellationRequest.from_json(payload.decode("utf-8"))
        task = self._incoming_handlers.get(cancel.request_id)
        if task is not None and not task.done():
            task.cancel()

    def _bind_handler_args(
        self, method: Callable[..., object], wire_args: list[object]
    ) -> list[object]:
        """Map wire args positionally onto the handler's parameters.

        Injects a `Message` for any `Message`-typed parameter (the .NET
        trailing-`Message` convention) and **ignores extra trailing wire
        args** — which is how an idiomatic .NET client's optional
        `CancellationToken` (serialized as one extra parameter per the
        `Message`/CT convention) is tolerated. A handler may declare `*args`
        to receive every wire argument. Missing args fall back to defaults.
        """
        plan = _binding_plan(method)
        message: Message[object] | None = None
        sentinel = object()
        wire = iter(wire_args)
        bound: list[object] = []
        for tag in plan:
            if tag == "message":
                if message is None:
                    message = Message(
                        client=self, request_timeout=self.request_timeout
                    )
                bound.append(message)
            elif tag == "varargs":
                bound.extend(wire)
            elif tag == "wire":
                nxt = next(wire, sentinel)
                if nxt is sentinel:
                    break  # out of wire args — remaining params use defaults
                bound.append(nxt)
            # "skip": keyword-only / **kwargs — not fillable positionally
        return bound

    async def _invoke_callback(self, req: Request) -> None:
        """Run the user's callback for an incoming Request, then send the Response."""
        try:
            handler = self._callbacks.get(req.endpoint)
            if handler is None:
                raise RuntimeError(
                    f"no callback registered for endpoint {req.endpoint!r}"
                )
            method = getattr(handler, req.method_name, None)
            if method is None or not callable(method):
                raise RuntimeError(
                    f"callback {req.endpoint!r} has no method "
                    f"{req.method_name!r}"
                )
            # Each parameter is an individually JSON-encoded string (wire gotcha).
            args = [json.loads(p) for p in req.parameters]
            call_args = self._bind_handler_args(method, args)
            result = method(*call_args)
            if inspect.isawaitable(result):
                result = await result
            data = None if result is None else json.dumps(result)
            resp = Response(request_id=req.id, data=data)
        except asyncio.CancelledError:
            # Server cancelled us. Send back a cancellation Error so the
            # server's pending future resolves (and matches .NET's
            # OperationCanceledException semantics).
            resp = Response(
                request_id=req.id,
                error=Error(
                    message="callback cancelled",
                    type_name="System.OperationCanceledException",
                ),
            )
        except BaseException as ex:
            resp = Response(
                request_id=req.id,
                error=Error(
                    message=str(ex) or type(ex).__name__,
                    type_name=type(ex).__name__,
                    stack_trace=traceback.format_exc(),
                ),
            )

        if self._closed:
            return
        try:
            await self._send_frame(
                MessageType.RESPONSE, resp.to_json().encode("utf-8")
            )
        except Exception:
            # Connection probably tore down — nothing to do.
            pass

    def _fail_pending(self, ex: BaseException) -> None:
        for fut in list(self._pending.values()):
            if not fut.done():
                fut.set_exception(ex)
        self._pending.clear()
