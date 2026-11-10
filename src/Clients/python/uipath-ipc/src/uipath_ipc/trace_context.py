"""W3C trace-context propagation (traceparent, tracestate, baggage).

Opt-in per endpoint object and direction (``IpcClient``/``IpcServer``
``telemetry_propagation=``, a `TelemetryPropagationPolicy`). Outgoing calls
carry, in precedence order, the explicit `use_trace_context` override, else the
current OpenTelemetry span context + baggage (only when ``opentelemetry-api``
is installed — the ``otel`` extra), else nothing; an override without a
traceparent takes the span's traceparent and tracestate. Incoming handlers run
under the received context: `incoming_trace_context` returns the raw values,
and with OpenTelemetry installed the handler's current span is the
(non-recording) remote parent.
"""

from __future__ import annotations

import contextlib
import contextvars
import enum
import re
from dataclasses import dataclass, field
from typing import Iterator, Mapping
from urllib.parse import quote, unquote

TRACEPARENT = "traceparent"
TRACESTATE = "tracestate"
BAGGAGE = "baggage"

_MAX_TRACESTATE_MEMBERS = 32
_MAX_BAGGAGE_MEMBERS = 64
_MAX_BAGGAGE_BYTES = 8192
# Trace Context §3.2: lowercase hex, version ff is invalid, non-zero ids; a higher
# version is read by its first 55 characters, which must then end or be followed
# by '-' (§3.2.4).
_TRACEPARENT = re.compile(
    r"(?!ff)([0-9a-f]{2})-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}(-.*)?",
    re.DOTALL,
)
# Trace Context §3.3.1.3.
_TRACESTATE_MEMBER = re.compile(
    r"(?:[a-z][a-z0-9_\-*/]{0,255}|[a-z0-9][a-z0-9_\-*/]{0,240}@[a-z][a-z0-9_\-*/]{0,13})"
    r"=[\x20-\x2b\x2d-\x3c\x3e-\x7e]{0,255}[\x21-\x2b\x2d-\x3c\x3e-\x7e]"
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
    """The traceparent breaks W3C Trace Context §3.2, or there is neither a
    traceparent nor a baggage. Crosses the wire as .NET's
    ``System.ArgumentException``, like the .NET implementation's error."""

    wire_type_name = "System.ArgumentException"


@dataclass(frozen=True, slots=True)
class TraceContext:
    """The W3C trace context of an IPC call. ``baggage`` is W3C Baggage:
    comma-separated, percent-encoded ``key=value``. ``traceparent`` is optional:
    W3C Baggage doesn't depend on Trace Context, so a baggage can travel alone;
    the ``tracestate`` travels only with a traceparent. Empty values normalize
    to ``None``. The repr shows the traceparent only.
    """

    traceparent: str | None = None
    tracestate: str | None = field(default=None, repr=False)
    baggage: str | None = field(default=None, repr=False)

    def __post_init__(self) -> None:
        if not self.traceparent:
            object.__setattr__(self, "traceparent", None)
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
    inside it — winning over OpenTelemetry. Without a ``traceparent``, its
    ``baggage`` goes with the current OpenTelemetry span's traceparent and
    tracestate, or alone when there is no span. Scopes nest, and an inner one
    replaces the outer one; exiting restores the previous value. Ignored by
    endpoints whose policy lacks ``OUTGOING``.

    Raises `InvalidTraceContextError` (a `ValueError`) when the ``traceparent``
    breaks W3C Trace Context §3.2, or when there is neither a ``traceparent``
    nor a ``baggage``."""
    if not isinstance(trace_context, TraceContext):
        raise TypeError("trace_context must be a TraceContext")
    if trace_context.traceparent is not None and not _is_valid_traceparent(
        trace_context.traceparent
    ):
        raise InvalidTraceContextError(
            f"{TRACEPARENT}: The traceparent must be a W3C traceparent."
        )
    if trace_context.traceparent is None and trace_context.baggage is None:
        raise InvalidTraceContextError(
            f"The trace context must have a {TRACEPARENT} or a {BAGGAGE}."
        )
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
    tc = _override.get()
    if tc is None:
        tc = _from_opentelemetry()
    elif tc.traceparent is None:
        # Without a traceparent, the scope's baggage goes with the span's trace.
        ambient = _from_opentelemetry(with_baggage=False)
        if ambient is not None and ambient.traceparent:
            tc = TraceContext(ambient.traceparent, ambient.tracestate, tc.baggage)
    if tc is None:
        return None
    headers: dict[str, str] = {}
    if tc.traceparent and _is_valid_traceparent(tc.traceparent):
        headers[TRACEPARENT] = tc.traceparent
        tracestate = tc.tracestate and _limit_tracestate(tc.tracestate)
        if tracestate:
            headers[TRACESTATE] = tracestate
    baggage = tc.baggage and _limit_baggage(tc.baggage)
    if baggage:
        headers[BAGGAGE] = baggage
    return headers or None


def from_headers(headers: Mapping[str, str] | None) -> TraceContext | None:
    """A traceparent that breaks W3C Trace Context is ignored, and so is its
    tracestate (§3.3); tracestate members that break their grammar are dropped.
    ``None`` when neither a traceparent nor a baggage is left."""
    if headers is None:
        return None
    traceparent = _get_header(headers, TRACEPARENT)
    if traceparent is not None and not _is_valid_traceparent(traceparent):
        traceparent = None
    received_tracestate = _get_header(headers, TRACESTATE) if traceparent else None
    tracestate = (
        ",".join(_valid_tracestate_members(received_tracestate))
        if received_tracestate
        else None
    )
    baggage = _get_header(headers, BAGGAGE)
    if not traceparent and not baggage:
        return None
    return TraceContext(traceparent, tracestate, baggage)


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


# --- W3C limits on send --------------------------------------------------------


def _limit_baggage(baggage: str) -> str | None:
    """At most 64 members and 8192 bytes, dropping whole members beyond that,
    and the ones a receiver couldn't parse."""
    kept: list[str] = []
    size = 0
    for raw in baggage.split(","):
        member = raw.strip(" \t")
        if _split_baggage_member(member) is None:
            continue
        member_size = len(member.encode("utf-8", "surrogatepass")) + (1 if kept else 0)
        if size + member_size > _MAX_BAGGAGE_BYTES:
            continue
        kept.append(member)
        size += member_size
        if len(kept) == _MAX_BAGGAGE_MEMBERS:
            break
    return ",".join(kept) or None


def _limit_tracestate(tracestate: str) -> str | None:
    """Trace Context §3.3.1.1: at most 32 members, so keep the first 32 valid
    ones."""
    return ",".join(_valid_tracestate_members(tracestate)[:_MAX_TRACESTATE_MEMBERS]) or None


def _is_valid_traceparent(value: str) -> bool:
    match = _TRACEPARENT.fullmatch(value)
    return match is not None and not (match[1] == "00" and match[2] is not None)


def _valid_tracestate_members(tracestate: str) -> list[str]:
    members = (member.strip(" \t") for member in tracestate.split(","))
    return [member for member in members if _TRACESTATE_MEMBER.fullmatch(member)]


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
    """Skips a member with no key, trims the key and value, and ignores the
    member's properties (after ``;``)."""
    pairs = (_split_baggage_member(member) for member in baggage.split(","))
    # W3C Baggage: a percent-escape that isn't valid UTF-8 decodes to U+FFFD
    # (unquote's default errors="replace").
    return [(unquote(pair[0]), unquote(pair[1])) for pair in pairs if pair is not None]


def _split_baggage_member(member: str) -> tuple[str, str] | None:
    key, separator, value = member.split(";", 1)[0].partition("=")
    key = key.strip(" \t")
    if not separator or not key:
        return None
    return key, value.strip(" \t")


# --- OpenTelemetry (optional) --------------------------------------------------


def _from_opentelemetry(with_baggage: bool = True) -> TraceContext | None:
    try:
        from opentelemetry import baggage, trace
    except Exception:
        return None
    span_context = trace.get_current_span().get_span_context()
    encoded_baggage = _encode_baggage(baggage.get_all()) if with_baggage else None
    if not span_context.is_valid:
        return TraceContext(baggage=encoded_baggage) if encoded_baggage else None
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
    return TraceContext(traceparent, tracestate, encoded_baggage)


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
    if trace_context is not None and trace_context.traceparent:
        carrier = {TRACEPARENT: trace_context.traceparent}
        if trace_context.tracestate:
            carrier[TRACESTATE] = trace_context.tracestate
        ctx = TraceContextTextMapPropagator().extract(carrier, context=ctx)
        span_context = trace.get_current_span(ctx).get_span_context()
        if span_context.is_valid and trace_context.tracestate:
            _incoming_tracestate.set(
                (span_context.trace_id, span_context.trace_state, trace_context.tracestate)
            )
    if trace_context is not None and trace_context.baggage:
        for key, value in _decode_baggage(trace_context.baggage):
            ctx = baggage.set_baggage(key, value, ctx)
    return otel_context.attach(ctx)
