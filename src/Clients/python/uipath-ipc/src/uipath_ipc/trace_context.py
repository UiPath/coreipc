"""W3C trace-context propagation (traceparent, tracestate, baggage).

Opt-in per endpoint object and direction (``IpcClient``/``IpcServer``
``telemetry_propagation=``, a `TelemetryPropagationPolicy`). Outgoing calls
carry, in precedence order, the explicit `use_trace_context` override, else the
current OpenTelemetry span context + baggage (only when ``opentelemetry-api``
is installed — the ``otel`` extra), else nothing. Incoming handlers run under
the received context: `incoming_trace_context` returns the raw values, and with
OpenTelemetry installed the handler's current span is the (non-recording)
remote parent.
"""

from __future__ import annotations

import contextlib
import contextvars
import enum
import re
from dataclasses import dataclass, field
from typing import Iterator, Mapping
from urllib.parse import quote

TRACEPARENT = "traceparent"
TRACESTATE = "tracestate"
BAGGAGE = "baggage"

_MAX_TRACESTATE_MEMBERS = 32
_MAX_BAGGAGE_MEMBERS = 64
_MAX_BAGGAGE_BYTES = 8192
_UTF8_ESCAPE = re.compile(
    r"%[0-7][0-9a-f]|%[cd][0-9a-f]%[89ab][0-9a-f]|%e[0-9a-f](?:%[89ab][0-9a-f]){2}"
    r"|%f[0-7](?:%[89ab][0-9a-f]){3}",
    re.IGNORECASE,
)


class TelemetryPropagationPolicy(enum.Flag):
    """The directions in which an endpoint object (`IpcClient`/`IpcServer`)
    propagates the W3C trace context. ``OUTGOING``: the calls it makes (a
    client's calls, a server's callbacks) carry it. ``INCOMING``: the calls it
    handles (a server's calls, a client's callbacks) read it and run under it.
    """

    NONE = 0
    INCOMING = 1
    OUTGOING = 2
    BOTH = INCOMING | OUTGOING


class InvalidTraceContextError(ValueError):
    """A trace-context value breaks its W3C rule. Crosses the wire as .NET's
    ``System.ArgumentException``, like the .NET implementation's error."""

    wire_type_name = "System.ArgumentException"


@dataclass(frozen=True, slots=True)
class TraceContext:
    """The W3C trace context of an IPC call. ``baggage`` is W3C Baggage:
    comma-separated, percent-encoded ``key=value``. Empty ``tracestate`` /
    ``baggage`` normalize to ``None``. The repr shows the traceparent only.
    """

    traceparent: str
    tracestate: str | None = field(default=None, repr=False)
    baggage: str | None = field(default=None, repr=False)

    def __post_init__(self) -> None:
        if not self.tracestate:
            object.__setattr__(self, "tracestate", None)
        if not self.baggage:
            object.__setattr__(self, "baggage", None)


_override: contextvars.ContextVar[TraceContext | None] = contextvars.ContextVar(
    "uipath_ipc_trace_context_override", default=None
)
_incoming: contextvars.ContextVar[TraceContext | None] = contextvars.ContextVar(
    "uipath_ipc_incoming_trace_context", default=None
)
# (trace_id, the TraceState OpenTelemetry extracted, raw received tracestate)
_incoming_tracestate: contextvars.ContextVar[tuple[int, object, str] | None] = (
    contextvars.ContextVar("uipath_ipc_incoming_tracestate", default=None)
)


@contextlib.contextmanager
def use_trace_context(trace_context: TraceContext) -> Iterator[TraceContext]:
    """Make `trace_context` the trace context of every outgoing call (forward
    calls and callbacks) made within this scope — including from tasks created
    inside it — winning over OpenTelemetry. Scopes nest; exiting restores the
    previous value. Ignored by endpoints whose policy lacks ``OUTGOING``.

    Raises `ValueError` when a value breaks its W3C limit: ``tracestate`` at
    most 32 list members, ``baggage`` at most 64 list members and 8192 UTF-8
    bytes. The ``traceparent`` isn't checked."""
    if not isinstance(trace_context, TraceContext):
        raise TypeError("trace_context must be a TraceContext")
    message = _validate(trace_context.tracestate, trace_context.baggage)
    if message is not None:
        raise InvalidTraceContextError(message)
    token = _override.set(trace_context)
    try:
        yield trace_context
    finally:
        _override.reset(token)


def incoming_trace_context() -> TraceContext | None:
    """The trace context received with the call being handled, or ``None``
    when none arrived, the handling endpoint's policy lacks ``INCOMING``, or
    no handler is running."""
    return _incoming.get()


def outgoing_headers() -> dict[str, str] | None:
    """The headers for an outgoing call made now, or ``None`` for none."""
    tc = _override.get() or _from_opentelemetry()
    if tc is None:
        return None
    headers = {TRACEPARENT: tc.traceparent}
    if tc.tracestate:
        headers[TRACESTATE] = tc.tracestate
    if tc.baggage:
        headers[BAGGAGE] = tc.baggage
    return headers


def from_headers(headers: Mapping[str, str] | None) -> TraceContext | None:
    """Read incoming headers as they are, unchecked. A missing traceparent
    means no trace context (everything else is then ignored)."""
    if headers is None:
        return None
    traceparent = _get_header(headers, TRACEPARENT)
    if traceparent is None:
        return None
    return TraceContext(
        traceparent, _get_header(headers, TRACESTATE), _get_header(headers, BAGGAGE)
    )


@contextlib.contextmanager
def incoming_scope(trace_context: TraceContext | None, enabled: bool) -> Iterator[None]:
    """Run an incoming handler under `trace_context`.

    The handler task's context is copied from the receive loop, i.e. from
    whoever opened the connection, so an override or incoming value seen there
    must not leak into it. The OpenTelemetry context is replaced too: the
    remote parent when `enabled` (``INCOMING``), else empty (also when none
    arrived or OpenTelemetry rejects the traceparent)."""
    override_token = _override.set(None)
    incoming_token = _incoming.set(trace_context)
    tracestate_token = _incoming_tracestate.set(None)
    otel_token = _attach_opentelemetry(trace_context if enabled else None)
    try:
        yield
    finally:
        if otel_token is not None:
            from opentelemetry import context as otel_context

            otel_context.detach(otel_token)
        _incoming_tracestate.reset(tracestate_token)
        _incoming.reset(incoming_token)
        _override.reset(override_token)


# --- validation --------------------------------------------------------------


def _validate(tracestate: str | None, baggage: str | None) -> str | None:
    if tracestate is not None and _count_members(tracestate) > _MAX_TRACESTATE_MEMBERS:
        return (
            f"{TRACESTATE}: The tracestate must have at most "
            f"{_MAX_TRACESTATE_MEMBERS} list members."
        )
    if baggage is not None:
        if _count_members(baggage) > _MAX_BAGGAGE_MEMBERS:
            return (
                f"{BAGGAGE}: The baggage must have at most "
                f"{_MAX_BAGGAGE_MEMBERS} list members."
            )
        if len(baggage.encode("utf-8", "surrogatepass")) > _MAX_BAGGAGE_BYTES:
            return (
                f"{BAGGAGE}: The baggage must be at most "
                f"{_MAX_BAGGAGE_BYTES} bytes (UTF-8)."
            )
    return None


def _count_members(value: str) -> int:
    return len(value.split(","))


def _get_header(headers: Mapping[str, str], name: str) -> str | None:
    value = headers.get(name)
    if value is None:
        for key, candidate in headers.items():
            if key.lower() == name:
                return candidate
    return value


def _encode_baggage(entries: Mapping[str, object]) -> str | None:
    # quote(safe="") leaves exactly RFC 3986's unreserved set, like .NET's
    # Uri.EscapeDataString, so both sides encode identically.
    encoded = ",".join(
        f"{quote(str(key), safe='')}={quote(str(value), safe='')}"
        for key, value in entries.items()
    )
    return encoded or None


def _decode_baggage(baggage: str) -> list[tuple[str, str]]:
    pairs = (member.split("=", 1) for member in baggage.split(","))
    return [(_unescape(key), _unescape(value)) for key, value in pairs]


def _unescape(value: str) -> str:
    # Like .NET's Uri.UnescapeDataString: escapes that aren't valid UTF-8 stay as is.
    def decode(match: re.Match[str]) -> str:
        try:
            return bytes.fromhex(match[0].replace("%", "")).decode("utf-8")
        except UnicodeDecodeError:
            return match[0]

    return _UTF8_ESCAPE.sub(decode, value)


# --- OpenTelemetry (optional) --------------------------------------------------


def _from_opentelemetry() -> TraceContext | None:
    try:
        from opentelemetry import baggage, trace
    except Exception:
        return None
    span_context = trace.get_current_span().get_span_context()
    if not span_context.is_valid:
        return None
    traceparent = (
        f"00-{span_context.trace_id:032x}-{span_context.span_id:016x}-"
        f"{span_context.trace_flags:02x}"
    )
    received = _incoming_tracestate.get()
    if (
        received is not None
        and received[0] == span_context.trace_id
        and received[1] == span_context.trace_state
    ):
        # Unchanged since receipt (child spans inherit it): forward it verbatim,
        # like .NET, rather than OpenTelemetry's filtered re-serialization.
        tracestate = received[2]
    else:
        tracestate = span_context.trace_state.to_header()
    return TraceContext(traceparent, tracestate, _encode_baggage(baggage.get_all()))


def _attach_opentelemetry(trace_context: TraceContext | None) -> object | None:
    try:
        from opentelemetry import baggage, trace
        from opentelemetry import context as otel_context
        from opentelemetry.trace.propagation.tracecontext import (
            TraceContextTextMapPropagator,
        )
    except Exception:
        return None
    ctx = otel_context.Context()
    if trace_context is not None:
        carrier = {TRACEPARENT: trace_context.traceparent}
        if trace_context.tracestate:
            carrier[TRACESTATE] = trace_context.tracestate
        ctx = TraceContextTextMapPropagator().extract(carrier, context=ctx)
        span_context = trace.get_current_span(ctx).get_span_context()
        if span_context.is_valid and trace_context.tracestate:
            _incoming_tracestate.set(
                (span_context.trace_id, span_context.trace_state, trace_context.tracestate)
            )
        if trace_context.baggage:
            for key, value in _decode_baggage(trace_context.baggage):
                ctx = baggage.set_baggage(key, value, ctx)
    return otel_context.attach(ctx)
