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
)

from ..hooks import BeforeCallHandler, CallInfo
from ..errors import EndpointNotFoundError, MethodNotFoundError, RemoteException
from ..markers import is_ipc_cancellable
from ..message import Message
from ..transport.base import ClientTransport
from ..wire import (
    MAX_PAYLOAD_BYTES,
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
from ..wire.serialization import resolve_hints

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

#: Upper bound a caller's cancellation waits for the best-effort
#: CancellationRequest to flush before giving up. A non-reading peer can't be
#: delivered to (and wouldn't act on the cancel anyway); local IPC flushes this
#: tiny frame far faster, so the bound only bites on a stalled peer.
_CANCEL_SEND_TIMEOUT = 2.0


def _dispatch_plan(method: Callable[..., object]) -> _DispatchPlan:
    """Compute (and cache) how to bind wire args onto a handler's parameters,
    and whether it's a one-way (`-> None`) method.

    Pass the CONTRACT method (an unbound function, `self` included): the impl is
    duck-typed and may omit annotations, so the plan — Message injection,
    one-way, and per-arg decode types — must come from the contract, not the
    bound instance method. A leading ``self``/``cls`` is skipped.
    """
    func = getattr(method, "__func__", method)
    cached = _dispatch_plan_cache.get(func)
    if cached is not None:
        return cached

    # Best-effort hint resolution: one unresolvable annotation (e.g. a
    # TYPE_CHECKING-only param type) degrades just that name, not every hint —
    # so the other params still decode and one-way (`-> None`) is still detected.
    hints = resolve_hints(func)
    params: list[_ParamPlan] = []
    for i, (name, param) in enumerate(inspect.signature(method).parameters.items()):
        if i == 0 and name in ("self", "cls"):
            continue  # unbound contract function carries the receiver param
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


def _peek_wire_id(payload: bytes, key: str) -> str | None:
    """Best-effort recovery of a correlation id (``RequestId`` for a Response,
    ``Id`` for a Request) from a frame whose full DTO failed to parse — so the
    correlated call can be failed (not left hanging) or the sender answered.
    Returns None if the payload isn't even parseable JSON with that key."""
    try:
        obj = json.loads(payload.decode("utf-8"))
    except (ValueError, UnicodeDecodeError):
        return None
    rid = obj.get(key) if isinstance(obj, dict) else None
    return rid if isinstance(rid, str) else None


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
        max_message_size: int = MAX_PAYLOAD_BYTES,
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
        #: Upper bound (bytes) on an INBOUND frame's payload, rejected by
        #: read_frame before allocation. Defaults to 2 MB (matching .NET's
        #: MaxReceivedMessageSize); configurable per client/server.
        self._max_message_size = max_message_size
        #: Awaited before dispatching each incoming request (server side).
        self._before_incoming_call = before_incoming_call
        self._pending: dict[str, asyncio.Future[Response]] = {}
        # request id -> (handler task, whether the contract method is
        # @ipc_cancellable). The flag gates whether a peer CancellationRequest
        # may cancel the handler (symmetric with the client's send gate).
        self._incoming_handlers: dict[str, tuple[asyncio.Task[None], bool]] = {}
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
        max_message_size: int = MAX_PAYLOAD_BYTES,
    ) -> IpcConnection:
        """Connect via the transport, wrap the stream in a new connection."""
        reader, writer = await transport.connect()
        conn = cls(
            reader,
            writer,
            callbacks=callbacks,
            request_timeout=request_timeout,
            max_message_size=max_message_size,
        )
        conn.start()
        return conn

    def start(self) -> None:
        """Begin the receive loop. Idempotent."""
        if self._receive_task is not None:
            return
        self._receive_task = asyncio.create_task(self._receive_loop())

    async def aclose(self) -> None:
        """Close the connection and fail/cancel any in-flight work. Idempotent,
        and still aborts the transport even if a prior send-timeout teardown
        already set `_closed` (that path closes the writer but does NOT abort)."""
        self._force_close()

    def _force_close(self) -> None:
        """Forceful, idempotent teardown shared by `aclose()` and the
        send-timeout path: mark closed, cancel the receive loop, run the local
        teardown, and ABORT the transport.

        The abort (vs a graceful `writer.close()`) is the crucial part on a
        non-reading peer: matching .NET (`Connection.Dispose` ->
        `Network.Dispose()`) and the TS client (`socket.destroy()`), it forces
        the transport down instead of awaiting a flush the deaf peer could stall
        forever. On the Windows ProactorEventLoop, `close()`+`wait_closed()`
        defers `connection_lost` until the write buffer drains — which never
        happens for a deaf peer — so without the abort the fd, the buffered
        bytes, and the still-blocked receive task all leak. `abort()` discards
        the buffer and returns immediately."""
        self._closed = True
        if self._receive_task is not None:
            self._receive_task.cancel()
        self._teardown()
        transport = getattr(self._writer, "transport", None)
        abort = getattr(transport, "abort", None)
        if abort is not None:
            try:
                abort()
            except Exception:
                pass

    def _teardown(self) -> None:
        """Idempotent local cleanup shared by `aclose` and the receive loop:
        cancel in-flight incoming handlers, close the writer, fail pending
        outgoing requests, and fire close callbacks. Does NOT touch the
        receive task (the loop calls this from its own `finally`)."""
        # Teardown cancels EVERY handler regardless of @ipc_cancellable — the
        # connection is going away, so in-flight work can't continue anyway.
        for task, _ in list(self._incoming_handlers.values()):
            task.cancel()
        self._incoming_handlers.clear()
        try:
            self._writer.close()
        except Exception:
            pass
        self._fail_pending(ConnectionError("connection closed"))
        self._notify_closed()

    def _abandon(self) -> None:
        """Synchronous best-effort cleanup for a connection abandoned without
        `aclose()` (e.g. its owning `IpcClient` was garbage-collected): mark
        closed and close the writer so the blocked receive loop unblocks (its
        `readexactly` raises) and its task can finish — otherwise the running
        task keeps the connection alive forever (an fd / task leak)."""
        self._closed = True
        try:
            self._writer.close()
        except Exception:
            pass

    async def _ensure_connected(self) -> tuple[IpcConnection, bool]:
        """This connection is already open (never a 'new' connection for hook
        purposes). Lets `_IpcProxy` drive a reach-back proxy directly off the
        connection (see `get_callback`)."""
        return self, False

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

    async def send_request(
        self, req: Request, *, propagate_cancellation: bool = False
    ) -> Response:
        """Send a request and await the matching response.

        If the awaiting task is cancelled, a best-effort `CancellationRequest`
        is sent to the peer with the matching id — but ONLY when
        `propagate_cancellation` is True (the call's contract method is
        `@ipc_cancellable`). Otherwise the cancel stays local: the peer isn't
        told to cancel, since without a `CancellationToken` it has nothing to
        honor it with. `CancelledError` is re-raised either way so cancellation
        still propagates locally.
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
            # Forward the cancel to the peer only for @ipc_cancellable methods;
            # otherwise the peer has no CancellationToken to act on it.
            if propagate_cancellation:
                # Send INLINE before re-raising — matching .NET, which initiates
                # the cancel-send at cancel time, before any subsequent dispose.
                # A detached task would lose the race to aclose() when the cancel
                # unwinds through `async with` (timeout / parent-cancel) and get
                # dropped. Bounded + best-effort so a non-reading peer can't wedge
                # the cancellation.
                try:
                    await asyncio.wait_for(
                        self._safe_send_cancellation(req.id),
                        timeout=_CANCEL_SEND_TIMEOUT,
                    )
                except Exception:
                    pass
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
        teardown_on_timeout: bool = True,
    ) -> None:
        """Write one frame atomically under the write lock, optionally bounded
        by a send deadline (the per-call `timeout`, else the connection's
        `_send_timeout`). On a non-reading peer, `drain()` blocks on backpressure
        and would wedge the shared writer for every queued frame; if the bound
        elapses we cancel the write (releasing the lock) so other frames aren't
        wedged. For a request send we then tear the connection down (it can't
        recover); for a best-effort send (`teardown_on_timeout=False` — RESPONSE
        / CANCELLATION) we just abandon that one frame and leave the connection
        up, mirroring .NET writing responses with CancellationToken.None. A
        non-positive/None bound is unbounded."""
        bound = timeout if timeout is not None else self._send_timeout
        if bound is not None and bound > 0:
            try:
                await asyncio.wait_for(self._locked_write(msg_type, payload), bound)
            except asyncio.TimeoutError:
                if teardown_on_timeout:
                    # A request send times out because the peer isn't reading, so
                    # the write buffer is full — a graceful writer.close() would
                    # defer connection_lost forever (Windows ProactorEventLoop).
                    # Force the connection down (abort the transport + cancel the
                    # receive loop), the same teardown aclose() does, so the fd /
                    # buffered bytes / receive task don't leak.
                    self._force_close()
                raise
        else:
            await self._locked_write(msg_type, payload)

    async def _locked_write(self, msg_type: MessageType, payload: bytes) -> None:
        async with self._write_lock:
            await write_frame(self._writer, msg_type, payload)

    async def _safe_send_cancellation(self, request_id: str) -> None:
        """Best-effort: send a CancellationRequest, swallow any errors. No
        closed-check — the caller sends this inline at cancel time (before
        aclose), and attempting it regardless mirrors .NET's fire-and-forget."""
        try:
            payload = (
                CancellationRequest(request_id=request_id)
                .to_json()
                .encode("utf-8")
            )
            await self._send_frame(
                MessageType.CANCELLATION_REQUEST, payload, teardown_on_timeout=False
            )
        except Exception as ex:
            # Best-effort, but don't vanish silently — .NET logs this too.
            _logger.debug("cancellation send for %s failed: %r", request_id, ex)

    # --- receive loop ------------------------------------------------------

    async def _receive_loop(self) -> None:
        try:
            while not self._closed:
                msg_type, payload = await read_frame(
                    self._reader, self._max_message_size
                )
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
                # NB: each handler isolates a malformed *content* payload itself
                # — read_frame already consumed the whole length-prefixed frame,
                # so the stream stays aligned; the handler logs it, fails just
                # the correlated call (RESPONSE) or answers the sender (REQUEST),
                # and the connection survives. An *internal* bug escaping a
                # handler instead propagates here and tears the connection down,
                # surfacing it rather than masking it as a "malformed frame".
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
        try:
            resp = Response.from_json(payload.decode("utf-8"))
        except (ValueError, KeyError, UnicodeDecodeError) as ex:
            # Malformed body: recover the id so the correlated waiter fails
            # fast instead of hanging silently until its timeout (the
            # connection stays up). If the id can't be recovered, the call
            # must rely on its own deadline.
            rid = _peek_wire_id(payload, "RequestId")
            _logger.warning("dropping malformed RESPONSE frame (id=%s): %r", rid, ex)
            fut = self._pending.get(rid) if rid is not None else None
            if fut is not None and not fut.done():
                fut.set_exception(ex)
            return
        fut = self._pending.get(resp.request_id)
        if fut is not None and not fut.done():
            fut.set_result(resp)

    def _handle_incoming_request(self, payload: bytes) -> None:
        """Dispatch an incoming Request to a registered callback in a task.

        Runs in a background task so the receive loop stays free for the
        next frame.
        """
        try:
            req = Request.from_json(payload.decode("utf-8"))
        except (ValueError, KeyError, UnicodeDecodeError) as ex:
            # Malformed body: recover the id and answer the sender with an Error
            # so it doesn't hang; otherwise drop the frame (connection stays up).
            rid = _peek_wire_id(payload, "Id")
            _logger.warning("dropping malformed REQUEST frame (id=%s): %r", rid, ex)
            if rid is not None:
                asyncio.create_task(self._try_send_response(Response(
                    request_id=rid,
                    error=Error(
                        message=f"malformed request: {ex}",
                        type_name="System.IO.InvalidDataException",
                    ),
                )))
            return
        # A peer may cancel this handler only if the hosted contract method is
        # @ipc_cancellable. Resolve it now (synchronously) so the flag is ready
        # the instant a CancellationRequest arrives — even before the handler
        # task has resolved its own method. Unknown endpoint/method -> not
        # cancellable (the handler will fail fast with its own error anyway).
        cancellable = False
        entry = self._callbacks.get(req.endpoint)
        if entry is not None:
            contract, _instance = entry
            cancellable = is_ipc_cancellable(contract, req.method_name)
        task = asyncio.create_task(self._invoke_callback(req))
        self._incoming_handlers[req.id] = (task, cancellable)
        task.add_done_callback(
            lambda _t, rid=req.id: self._incoming_handlers.pop(rid, None)
        )

    def _handle_incoming_cancellation(self, payload: bytes) -> None:
        """Cancel an in-flight incoming-request handler by id."""
        try:
            cancel = CancellationRequest.from_json(payload.decode("utf-8"))
        except (ValueError, KeyError, UnicodeDecodeError) as ex:
            _logger.warning("dropping malformed CANCELLATION frame: %r", ex)
            return
        entry = self._incoming_handlers.get(cancel.request_id)
        if entry is None:
            return
        task, cancellable = entry
        # Honor the peer's cancel ONLY for @ipc_cancellable methods; an unmarked
        # method can't be cancelled remotely (symmetric with the client, which
        # won't even send a CancellationRequest for one). cancel() is a safe
        # no-op on an already-done task, so no done() check is needed.
        if cancellable:
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
                # *args elements pass through UNDECODED — materializing them to a
                # declared element type (`*args: T`) is unsupported (no .NET/TS use
                # case). Use explicit, individually-typed params. (README: Not
                # supported / undefined behaviour.)
                pos.extend(wire)
            elif tag == "wire":
                nxt = next(wire, _MISSING)
                if nxt is not _MISSING:
                    # Materialize into the contract's declared param type so a
                    # handler with a dataclass param receives an instance (and
                    # value types decode) — symmetric with the result path. A
                    # missing required field raises, aborting dispatch with an
                    # error (loud-on-drift); give optional fields a default.
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
            # Plan from the CONTRACT method (cmethod), not the duck-typed impl:
            # an unannotated impl would otherwise lose Message injection,
            # one-way detection, and per-arg decode types.
            plan = _dispatch_plan(cmethod)
            # Each parameter is an individually JSON-encoded string (wire gotcha).
            args = [json.loads(p) for p in req.parameters]
            # Bind FIRST: decode each arg to its declared type and inject the
            # Message, THEN fire the hook — so BeforeIncomingCall sees the typed,
            # Message-injected arguments, matching .NET (GetArguments deserializes
            # + injects before BeforeCall). A binding failure therefore precedes
            # the hook, which is also .NET's ordering.
            pos, kwargs = self._bind_handler_args(plan.params, args)
            # BeforeIncomingCall hook (server side); raising aborts the call
            # and is surfaced to the caller as an Error response.
            if self._before_incoming_call is not None:
                hook = self._before_incoming_call(
                    CallInfo(req.endpoint, req.method_name, tuple(pos))
                )
                if inspect.isawaitable(hook):
                    await hook
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
                    # Bound by the same effective timeout as request/response
                    # handlers (the ack already went out, so on expiry we just
                    # log rather than reply) — so a one-way handler can't run
                    # unbounded while a value-returning one would be cut off.
                    effective = self._effective_timeout(req)
                    if effective is not None and effective > 0:
                        await asyncio.wait_for(result, timeout=effective)
                    else:
                        await result
            except (asyncio.CancelledError, SystemExit, KeyboardInterrupt):
                # Cancellation tears the task down; fatal signals must still
                # propagate (process termination) even for fire-and-forget.
                raise
            except BaseException:
                _logger.exception(
                    "one-way %s.%s failed (or timed out)", req.endpoint, req.method_name
                )

    def _effective_timeout(self, req: Request) -> float | None:
        """The handler deadline: the wire `TimeoutInSeconds` if set, else the
        server default. ``None``/non-positive means no bound; a negative wire
        value (`.NET` ``Timeout.InfiniteTimeSpan``) means infinite.

        NOTE: only bounds *awaitable* (``async def``) handlers — a plain `def`
        handler runs to completion inline (and blocks the event loop); hosted
        handlers should be ``async`` (mirrors .NET requiring a `Task` return)."""
        return (
            req.timeout_in_seconds
            if req.timeout_in_seconds is not None
            else self._inbound_request_timeout
        )

    async def _run_handler(
        self,
        method: Callable[..., object],
        pos: list[object],
        kwargs: dict[str, object],
        req: Request,
    ) -> object:
        """Invoke a request/response handler, bounding an awaited result by the
        effective timeout (see `_effective_timeout`).

        The deadline is enforced authoritatively: a handler that catches its
        ``CancelledError`` and returns a value can't turn a timeout into a late
        success (a plain ``wait_for`` would hand that value back). On expiry we
        cancel and raise ``TimeoutError`` regardless of whether the handler
        observed the cancellation — matching .NET, which enforces the deadline
        no matter what the handler does with the token."""
        result = method(*pos, **kwargs)
        if not inspect.isawaitable(result):
            # Sync handler: it already ran to completion inline on the event
            # loop. Hosted handlers should be `async def` (see the IpcServer/
            # IpcClient docs) — a sync one blocks the whole connection and isn't
            # bounded by the timeout.
            return result
        effective = self._effective_timeout(req)
        if effective is None or effective <= 0:
            return await result
        task = asyncio.ensure_future(result)
        try:
            done, _pending = await asyncio.wait({task}, timeout=effective)
        except asyncio.CancelledError:
            # A peer CancellationRequest cancelled this dispatch. asyncio.wait
            # does NOT propagate cancellation to the awaited task, so cancel the
            # inner handler explicitly and await its teardown before re-raising
            # — otherwise it runs orphaned to completion.
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            raise
        if task not in done:
            # Deadline elapsed — authoritative even if the handler swallows its
            # CancelledError. gather() awaits the cancelled task's teardown and
            # retrieves any exception (no "never retrieved" warning).
            task.cancel()
            await asyncio.gather(task, return_exceptions=True)
            raise asyncio.TimeoutError()
        return task.result()

    async def _try_send_response(self, resp: Response) -> None:
        """Best-effort RESPONSE send; no-op if the connection tore down."""
        if self._closed:
            return
        try:
            await self._send_frame(
                MessageType.RESPONSE,
                resp.to_json().encode("utf-8"),
                teardown_on_timeout=False,
            )
        except Exception:
            # Connection probably tore down — nothing to do.
            pass

    def _fail_pending(self, ex: BaseException) -> None:
        for fut in list(self._pending.values()):
            if not fut.done():
                fut.set_exception(ex)
        self._pending.clear()
