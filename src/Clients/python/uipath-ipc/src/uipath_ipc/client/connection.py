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
from typing import (
    Callable,
    NamedTuple,
    TypeVar,
    Union,
    cast,
    get_args,
    get_origin,
    get_type_hints,
)

from ..hooks import BeforeCallHandler, CallInfo
from ..errors import EndpointNotFoundError, MethodNotFoundError, RemoteException
from ..message import Message
from ..transport.base import ClientTransport
from ..wire import (
    CancellationRequest,
    Error,
    MessageType,
    Request,
    Response,
    from_wire,
    read_frame,
    to_wire,
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


# A handler's dispatch plan: one (tag, name, hint) per parameter (self
# excluded) plus whether the method is one-way. tag is one of:
#   "wire"    -> take the next positional wire argument, decoded to `hint`
#   "message" -> consume the next wire slot (if present) and build a Message
#                from its `Payload`; injected by KEYWORD so it works whether
#                the Message param is trailing or keyword-only
#   "varargs" -> *args: absorb all remaining wire arguments
#   "skip"    -> **kwargs / non-Message keyword-only: not fillable from wire
# `one_way` mirrors .NET's non-generic `Task`: a method explicitly annotated
# `-> None` acks immediately and runs detached. A *missing* return annotation
# is NOT treated as one-way (we can't tell, so it stays request/response).
# Cached weakly by the underlying function so it's computed once per method.
_ParamPlan = tuple[str, str, object]


class _DispatchPlan(NamedTuple):
    params: tuple[_ParamPlan, ...]
    one_way: bool


_dispatch_plan_cache: "weakref.WeakKeyDictionary[object, _DispatchPlan]" = (
    weakref.WeakKeyDictionary()
)

#: Sentinel for "no more wire args" (avoids allocating one per request).
_MISSING = object()


def _dispatch_plan(method: Callable[..., object]) -> _DispatchPlan:
    """Compute (and cache) how to bind wire args onto a handler's parameters,
    and whether it's a one-way (`-> None`) method."""
    func = getattr(method, "__func__", method)
    cached = _dispatch_plan_cache.get(func)
    if cached is not None:
        return cached

    try:
        hints = get_type_hints(func)
    except Exception:
        hints = {}
    params: list[_ParamPlan] = []
    for name, param in inspect.signature(method).parameters.items():
        hint = hints.get(name, param.annotation)
        if hint is inspect.Parameter.empty:
            hint = None
        if param.kind is inspect.Parameter.VAR_POSITIONAL:
            params.append(("varargs", name, None))
        elif param.kind is inspect.Parameter.VAR_KEYWORD:
            params.append(("skip", name, None))
        # Check Message BEFORE keyword-only so a keyword-only Message is still
        # injected. A positional Message rides one wire slot and binds in
        # position (so a non-trailing Message keeps later args aligned); a
        # keyword-only Message is a reach-back handle with no wire slot.
        elif _is_message_annotation(hint):
            if param.kind is inspect.Parameter.KEYWORD_ONLY:
                params.append(("message_kw", name, hint))
            else:
                params.append(("message", name, hint))
        elif param.kind is inspect.Parameter.KEYWORD_ONLY:
            params.append(("skip", name, None))
        else:
            params.append(("wire", name, hint))
    one_way = "return" in hints and hints["return"] is type(None)
    result = _DispatchPlan(tuple(params), one_way)
    try:
        _dispatch_plan_cache[func] = result
    except TypeError:
        pass  # builtins / unweakreferenceable callables: just don't cache
    return result


def _format_traceback(exc: BaseException) -> str:
    return "".join(traceback.format_exception(type(exc), exc, exc.__traceback__))


def _error_from_exception(exc: BaseException) -> Error:
    """Build a wire `Error` from a handler exception, preserving the cause
    chain (`__cause__`, else `__context__`) so the peer's `RemoteException`
    reproduces it — the receive side (`RemoteException.from_error`) already
    recurses `inner_error`.

    A re-raised `RemoteException` (a handler that let a reach-back failure
    propagate) is forwarded verbatim — its original type name, message, stack
    trace, and inner chain — rather than collapsing to ``RemoteException``,
    mirroring .NET reusing an already-remote exception type.
    """
    if isinstance(exc, RemoteException):
        inner = _error_from_exception(exc.inner) if exc.inner is not None else None
        return Error(
            message=exc.message,
            stack_trace=exc.stack_trace,
            type_name=exc.type_name,
            inner_error=inner,
        )
    cause = exc.__cause__ or exc.__context__
    inner = _error_from_exception(cause) if isinstance(cause, BaseException) else None
    return Error(
        message=str(exc) or type(exc).__name__,
        stack_trace=_format_traceback(exc),
        # Dispatch errors carry their .NET wire type name so .NET callers can
        # match with RemoteException.Is<T>().
        type_name=getattr(exc, "wire_type_name", None) or type(exc).__name__,
        inner_error=inner,
    )


class IpcConnection:
    """One duplex stream + the bidirectional request/response dispatcher."""

    def __init__(
        self,
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        callbacks: dict[str, tuple[type, object]] | None = None,
        request_timeout: float | None = None,
        before_incoming_call: BeforeCallHandler | None = None,
        inbound_request_timeout: float | None = None,
        send_timeout: float | None = None,
    ) -> None:
        self._reader = reader
        self._writer = writer
        #: endpoint name -> (contract type, hosted instance). The contract is
        #: kept so dispatch can resolve an incoming method against it (and only
        #: it), mirroring the client proxy's `inspect.getattr_static` guard.
        self._callbacks: dict[str, tuple[type, object]] = dict(callbacks or {})
        #: Default timeout for reach-back proxies built via `get_callback`.
        self.request_timeout = request_timeout
        #: Server-side fallback bound for an inbound handler when the wire
        #: Request carries no explicit timeout (mirrors .NET RequestTimeout).
        self._inbound_request_timeout = inbound_request_timeout
        #: Optional bound on a single frame write. A non-reading peer can block
        #: drain() on backpressure forever and wedge the shared writer for every
        #: queued frame; on expiry we tear the connection down (None = unbound).
        self._send_timeout = send_timeout
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
        callbacks: dict[str, tuple[type, object]] | None = None,
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
            # If a successful response beat the cancellation, deliver it rather
            # than discarding it — mirroring .NET, where the response and the
            # cancellation arbitrate over the same request slot and a result
            # that arrived first wins (Connection.CancelRequest can't override
            # an already-completed request). Also skip the now-pointless
            # CancellationRequest for a call the peer already answered.
            if fut.done() and not fut.cancelled() and fut.exception() is None:
                return fut.result()
            asyncio.create_task(self._safe_send_cancellation(req.id))
            raise
        finally:
            self._pending.pop(req.id, None)

    # --- frame I/O ---------------------------------------------------------

    async def _send_frame(
        self,
        msg_type: MessageType,
        payload: bytes,
        *,
        timeout: float | None = None,
    ) -> None:
        """Write one frame atomically under the write lock, optionally bounded
        by a send deadline (the per-call `timeout`, else the connection's
        `_send_timeout`). On a non-reading peer, `drain()` blocks on backpressure
        and would wedge the shared writer for every queued frame; if the bound
        elapses, tear the connection down rather than wedge forever — mirroring
        .NET's dispose-on-send-cancel. A non-positive/None bound is unbounded."""
        bound = timeout if timeout is not None else self._send_timeout
        if bound is not None and bound > 0:
            try:
                await asyncio.wait_for(self._locked_write(msg_type, payload), bound)
            except asyncio.TimeoutError:
                self._closed = True
                self._teardown()
                raise
        else:
            await self._locked_write(msg_type, payload)

    async def _locked_write(self, msg_type: MessageType, payload: bytes) -> None:
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
        self, params: tuple[_ParamPlan, ...], wire_args: list[object]
    ) -> tuple[list[object], dict[str, object]]:
        """Map wire args onto a handler's parameters; return (positional, kwargs).

        - A non-`Message` parameter takes the next positional wire arg, decoded
          to its declared type via `from_wire` (so a `bytes`/`datetime`/`UUID`
          parameter receives the object, not the raw base64/ISO string). A
          handler may declare `*args` to absorb every remaining wire arg.
        - A `Message` parameter consumes the next wire slot — a `Message` is one
          declared argument on the wire (`{}` or `{"Payload": ...}`) — reads its
          `Payload`, and is injected by **keyword** (so it works whether the
          Message is trailing or keyword-only). If the wire is already exhausted
          (a reach-back `Message` the contract doesn't declare), a fresh one is
          built. Consuming the slot keeps later positional args aligned wherever
          the Message sits.
        - **Extra trailing wire args are ignored.** An idiomatic .NET client
          serializes one wire param per declared argument including a trailing
          `CancellationToken` (as `""`); ignoring the surplus tolerates that.
          Missing args fall back to their defaults.
        """
        wire = iter(wire_args)
        pos: list[object] = []
        kwargs: dict[str, object] = {}
        for tag, name, hint in params:
            if tag == "message":
                # Positional Message: consume its wire slot (if present), read
                # the Payload, and bind IN POSITION so a non-trailing Message
                # doesn't shift later args. A reach-back Message the contract
                # doesn't declare is trailing -> wire exhausted -> a fresh one.
                nxt = next(wire, _MISSING)
                body = nxt if isinstance(nxt, dict) else None
                pos.append(Message(
                    payload=(body or {}).get("Payload"),
                    client=self,
                    request_timeout=self.request_timeout,
                ))
            elif tag == "message_kw":
                # Keyword-only Message: a reach-back handle, no wire slot.
                kwargs[name] = Message(
                    client=self, request_timeout=self.request_timeout
                )
            elif tag == "varargs":
                pos.extend(wire)
            elif tag == "wire":
                nxt = next(wire, _MISSING)
                if nxt is not _MISSING:
                    pos.append(from_wire(nxt, hint))
                # else: out of wire args — let this param use its default, but
                # keep scanning so later Message params are still injected.
            # "skip": **kwargs / non-Message keyword-only — not fillable here.
        return pos, kwargs

    async def _invoke_callback(self, req: Request) -> None:
        """Run the user's handler for an incoming Request, then reply.

        Resolution, arg decoding, the before-call hook, and binding all run
        before we branch: a failure there returns an Error to the caller even
        for a one-way method (the peer is still awaiting the ack). A one-way
        (`-> None`) handler is acked immediately and then run detached with its
        exception only logged — mirroring .NET's non-generic `Task`.
        """
        deferred_one_way: tuple[Callable[..., object], list, dict] | None = None
        try:
            entry = self._callbacks.get(req.endpoint)
            if entry is None:
                raise EndpointNotFoundError(
                    f"no callback registered for endpoint {req.endpoint!r}"
                )
            contract, handler = entry
            # Resolve the method against the CONTRACT (rejecting private/dunder
            # names), then bind to the live instance — so a peer can only reach
            # declared contract methods, never arbitrary instance attributes.
            cmethod = (
                None
                if req.method_name.startswith("_")
                else inspect.getattr_static(contract, req.method_name, None)
            )
            if cmethod is None or not callable(cmethod):
                raise MethodNotFoundError(
                    f"callback {req.endpoint!r} has no method "
                    f"{req.method_name!r}"
                )
            method = getattr(handler, req.method_name)
            plan = _dispatch_plan(method)
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
            pos, kwargs = self._bind_handler_args(plan.params, args)
            if plan.one_way:
                # Ack now (empty Data, like .NET's Response.Success(req, "")),
                # then run the handler after responding (below).
                deferred_one_way = (method, pos, kwargs)
                resp = Response(request_id=req.id, data="")
            else:
                result = await self._run_handler(method, pos, kwargs, req)
                data = None if result is None else json.dumps(to_wire(result))
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
        except asyncio.TimeoutError:
            # The handler overran the effective request timeout — answer with
            # the .NET-typed error so a .NET caller sees a TimeoutException.
            resp = Response(
                request_id=req.id,
                error=Error(
                    message=f"{req.method_name} timed out.",
                    type_name="System.TimeoutException",
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
            resp = Response(request_id=req.id, error=_error_from_exception(ex))
            if isinstance(ex, (SystemExit, KeyboardInterrupt)):
                await self._try_send_response(resp)
                raise

        await self._try_send_response(resp)

        # One-way: run the handler AFTER the ack, still in this (tracked) task,
        # with its failure logged rather than returned — .NET's fire-and-forget.
        if deferred_one_way is not None:
            method, pos, kwargs = deferred_one_way
            try:
                result = method(*pos, **kwargs)
                if inspect.isawaitable(result):
                    await result
            except (asyncio.CancelledError, SystemExit, KeyboardInterrupt):
                # Cancellation tears the task down; fatal signals must still
                # propagate (process termination) even for fire-and-forget.
                raise
            except BaseException:
                _logger.exception(
                    "one-way %s.%s failed", req.endpoint, req.method_name
                )

    async def _run_handler(
        self,
        method: Callable[..., object],
        pos: list[object],
        kwargs: dict[str, object],
        req: Request,
    ) -> object:
        """Invoke a request/response handler, bounding an awaited result by the
        effective timeout: the wire `TimeoutInSeconds` if set, else the server
        default; ``None``/non-positive means no bound, a negative wire value
        (`.NET` ``Timeout.InfiniteTimeSpan``) means infinite."""
        result = method(*pos, **kwargs)
        if not inspect.isawaitable(result):
            return result
        effective = req.timeout_in_seconds
        if effective is None:
            effective = self._inbound_request_timeout
        if effective is not None and effective > 0:
            return await asyncio.wait_for(result, timeout=effective)
        return await result

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
