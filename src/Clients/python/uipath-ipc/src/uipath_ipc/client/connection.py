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
import logging
import traceback
import types
import weakref
from typing import Callable, TypeVar, Union, cast, get_args, get_origin, get_type_hints

from ..hooks import BeforeCallHandler, CallInfo
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

_logger = logging.getLogger(__name__)

#: Invoked once with the connection when it closes (e.g. to prune it from a
#: server's live-connection set). Should be synchronous and must not raise.
CloseCallback = Callable[["IpcConnection"], object]


_UNION_ORIGINS: tuple[object, ...] = (
    (Union, types.UnionType) if hasattr(types, "UnionType") else (Union,)
)


def _is_message_annotation(annotation: object) -> bool:
    """True if a parameter annotation refers to `Message`, `Message[T]`, or an
    `Optional`/union containing one (e.g. `Message | None`)."""
    if annotation is Message:
        return True
    if isinstance(annotation, str):
        # `from __future__ import annotations` leaves annotations as strings
        # when get_type_hints can't resolve them; match by spelling.
        s = annotation.replace(" ", "")
        return (
            s == "Message"
            or s.startswith("Message[")
            or s.startswith("Optional[Message")
            or "Message|None" in s
        )
    origin = get_origin(annotation)
    if origin is Message:
        return True
    if origin in _UNION_ORIGINS:
        return any(_is_message_annotation(arg) for arg in get_args(annotation))
    return False


# A handler's argument-binding plan: one (tag, name) per parameter (self
# excluded). tag is one of:
#   "wire"    -> take the next positional wire argument
#   "message" -> inject a Message by KEYWORD (consumes no wire argument), so it
#                works whether the Message param is trailing or keyword-only
#   "varargs" -> *args: absorb all remaining wire arguments
#   "skip"    -> **kwargs / non-Message keyword-only: not fillable from wire
# Cached weakly by the underlying function so it's computed once per method.
_BindingPlan = tuple[tuple[str, str], ...]
_binding_plan_cache: "weakref.WeakKeyDictionary[object, _BindingPlan]" = (
    weakref.WeakKeyDictionary()
)

#: Sentinel for "no more wire args" (avoids allocating one per request).
_MISSING = object()


def _binding_plan(method: Callable[..., object]) -> _BindingPlan:
    """Compute (and cache) how to map wire args onto a handler's parameters."""
    func = getattr(method, "__func__", method)
    cached = _binding_plan_cache.get(func)
    if cached is not None:
        return cached

    try:
        hints = get_type_hints(func)
    except Exception:
        hints = {}
    plan: list[tuple[str, str]] = []
    for name, param in inspect.signature(method).parameters.items():
        if param.kind is inspect.Parameter.VAR_POSITIONAL:
            plan.append(("varargs", name))
        elif param.kind is inspect.Parameter.VAR_KEYWORD:
            plan.append(("skip", name))
        # Check Message BEFORE keyword-only so a keyword-only Message is still
        # injected (it's passed by keyword anyway).
        elif _is_message_annotation(hints.get(name, param.annotation)):
            plan.append(("message", name))
        elif param.kind is inspect.Parameter.KEYWORD_ONLY:
            plan.append(("skip", name))
        else:
            plan.append(("wire", name))
    result = tuple(plan)
    try:
        _binding_plan_cache[func] = result
    except TypeError:
        pass  # builtins / unweakreferenceable callables: just don't cache
    return result


class IpcConnection:
    """One duplex stream + the bidirectional request/response dispatcher."""

    def __init__(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        callbacks: dict[str, object] | None = None,
        request_timeout: float | None = None,
        before_incoming_call: BeforeCallHandler | None = None,
    ) -> None:
        self._reader = reader
        self._writer = writer
        self._callbacks: dict[str, object] = dict(callbacks or {})
        #: Default timeout for reach-back proxies built via `get_callback`.
        self.request_timeout = request_timeout
        #: Awaited before dispatching each incoming request (server side).
        self._before_incoming_call = before_incoming_call
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
        self._teardown()
        try:
            await self._writer.wait_closed()
        except Exception:
            pass

    def _teardown(self) -> None:
        """Idempotent local cleanup shared by `aclose` and the receive loop:
        cancel in-flight incoming handlers, close the writer, fail pending
        outgoing requests, and fire close callbacks. Does NOT touch the
        receive task (the loop calls this from its own `finally`)."""
        for task in list(self._incoming_handlers.values()):
            task.cancel()
        self._incoming_handlers.clear()
        try:
            self._writer.close()
        except Exception:
            pass
        self._fail_pending(ConnectionError("connection closed"))
        self._notify_closed()

    async def _ensure_connected(self) -> IpcConnection:
        """This connection is already open. Lets `_IpcProxy` drive a reach-back
        proxy directly off the connection (see `get_callback`)."""
        return self

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

        # IpcConnection itself satisfies what _IpcProxy needs from a client
        # (`_ensure_connected` + `request_timeout`), so no adapter is required.
        return cast(T, _IpcProxy(self, contract))

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
                else:
                    # UPLOAD_REQUEST / DOWNLOAD_RESPONSE (streams) are out of
                    # scope; their frame is followed by a length + raw bytes we
                    # can't consume, so fail closed instead of desyncing.
                    raise ValueError(f"unsupported message type {msg_type!r}")
        except asyncio.CancelledError:
            raise
        except (asyncio.IncompleteReadError, ConnectionResetError, OSError) as ex:
            _logger.debug("receive loop ended (transport closed): %r", ex)
            self._fail_pending(ex)
        except Exception as ex:  # noqa: BLE001
            # Unexpected: a protocol/parse error or a genuine bug. The futures
            # channel only surfaces this when a call is in flight, so log it.
            _logger.exception("receive loop failed")
            self._fail_pending(ex)
        finally:
            # Mark closed so the owning IpcClient knows to re-dial on next call,
            # then run the shared teardown. On peer disconnect the connection is
            # pruned from any owning IpcServer, so aclose() won't run for it —
            # this is the only cleanup it gets (and it must close the writer so
            # the transport doesn't leak).
            self._closed = True
            self._teardown()

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
    ) -> tuple[list[object], dict[str, object]]:
        """Map wire args onto a handler's parameters; return (positional, kwargs).

        - Non-`Message` parameters are filled positionally from the wire, in
          order. A handler may declare `*args` to receive every remaining arg.
        - A `Message` parameter is injected by **keyword** (so it works whether
          it's trailing or keyword-only) and consumes no wire arg. Inject the
          caller handle there — conventionally the last parameter.
        - **Extra trailing wire args are ignored.** An idiomatic .NET client
          serializes one wire param per declared argument including a trailing
          `CancellationToken` (as `""`); ignoring the surplus tolerates that.
          Note this is positional: if a handler declares more optional params
          than the caller's contract has real args, a surplus value (e.g. the
          CT placeholder) can land on an optional param instead of its default.
          Missing args fall back to their defaults.
        """
        plan = _binding_plan(method)
        message: Message[object] | None = None
        wire = iter(wire_args)
        pos: list[object] = []
        kwargs: dict[str, object] = {}
        for tag, name in plan:
            if tag == "message":
                if message is None:
                    message = Message(
                        client=self, request_timeout=self.request_timeout
                    )
                kwargs[name] = message
            elif tag == "varargs":
                pos.extend(wire)
            elif tag == "wire":
                nxt = next(wire, _MISSING)
                if nxt is not _MISSING:
                    pos.append(nxt)
                # else: out of wire args — let this param use its default, but
                # keep scanning so later Message params are still injected.
            # "skip": **kwargs / non-Message keyword-only — not fillable here.
        return pos, kwargs

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
            # BeforeIncomingCall hook (server side); raising aborts the call
            # and is surfaced to the caller as an Error response.
            if self._before_incoming_call is not None:
                hook = self._before_incoming_call(
                    CallInfo(req.endpoint, req.method_name, tuple(args))
                )
                if inspect.isawaitable(hook):
                    await hook
            pos, kwargs = self._bind_handler_args(method, args)
            result = method(*pos, **kwargs)
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
            # Always answer the peer so its pending future never hangs — but
            # unlike C#'s `catch (Exception)`, BaseException also catches
            # SystemExit/KeyboardInterrupt; re-raise those after responding so
            # process-termination signals still propagate.
            _logger.exception(
                "callback %s.%s failed", req.endpoint, req.method_name
            )
            resp = Response(
                request_id=req.id,
                error=Error(
                    message=str(ex) or type(ex).__name__,
                    type_name=type(ex).__name__,
                    stack_trace=traceback.format_exc(),
                ),
            )
            if isinstance(ex, (SystemExit, KeyboardInterrupt)):
                await self._try_send_response(resp)
                raise

        await self._try_send_response(resp)

    async def _try_send_response(self, resp: Response) -> None:
        """Best-effort RESPONSE send; no-op if the connection tore down."""
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
