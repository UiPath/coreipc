"""W3C trace-context propagation (traceparent, tracestate, baggage).

Opt-in per endpoint object and direction (``IpcClient``/``IpcServer``
``telemetry_propagation=``, a `TelemetryPropagationPolicy`). Outgoing calls
carry, in precedence order, the explicit `use_trace_context` override, else the
current OpenTelemetry span context + baggage (only when ``opentelemetry-api``
is installed — the ``otel`` extra), else nothing; an override without a
traceparent takes the span's traceparent and tracestate. Incoming handlers run
under the received context: `incoming_trace_context` returns the received
values, and with OpenTelemetry installed the handler's current span is the
(non-recording) remote parent.

The headers are read and written as .NET's DiagnosticSource 10 W3CPropagator
does, so the .NET, JS and Python clients send the same bytes.
"""

from __future__ import annotations

import contextlib
import contextvars
import enum
import logging
import re
from dataclasses import dataclass, field
from types import SimpleNamespace
from typing import Any, Iterator, Mapping
from urllib.parse import quote, unquote

TRACEPARENT = "traceparent"
TRACESTATE = "tracestate"
BAGGAGE = "baggage"

_MAX_TRACESTATE_LENGTH = 256
_MAX_BAGGAGE_MEMBERS = 64
_MAX_BAGGAGE_LENGTH = 8192
# Trace Context §3.2, read by the first 55 characters: lowercase hex, version ff
# is invalid, non-zero ids. Then a version 00 must end, and a higher one must end
# or go on with '-'.
_TRACEPARENT = re.compile(
    r"(?!ff)[0-9a-f]{2}-(?!0{32})[0-9a-f]{32}-(?!0{16})[0-9a-f]{16}-[0-9a-f]{2}"
)
_TRACESTATE_KEY = re.compile(r"[a-z0-9][a-z0-9_\-*/@]*")
# 0x21-0x7E without ',' and '=': W3CPropagator allows no space inside a value.
_TRACESTATE_VALUE = re.compile(r"[\x21-\x2b\x2d-\x3c\x3e-\x7e]+")
# An RFC 7230 token.
_BAGGAGE_KEY = re.compile(r"[!#$%&'*+\-.^_`|~0-9A-Za-z]+")
_BAD_ESCAPE = re.compile(r"%(?![0-9A-Fa-f]{2})")
# The baggage-octets other than '%'; every other UTF-8 byte is percent-encoded.
_BAGGAGE_VALUE_SAFE = "!#$&'()*+/:<=>?@[]^`{|}~"


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
# (trace_id, the TraceState OpenTelemetry extracted, the received tracestate)
_incoming_tracestate: contextvars.ContextVar[tuple[int, object, str] | None] = (
    contextvars.ContextVar("uipath_ipc_incoming_tracestate", default=None)
)


@contextlib.contextmanager
def use_trace_context(trace_context: TraceContext | None) -> Iterator[TraceContext | None]:
    """Make `trace_context` the trace context of every outgoing call (forward
    calls and callbacks) made within this scope — including from tasks created
    inside it — winning over OpenTelemetry. Without a ``traceparent``, its
    ``baggage`` goes with the current OpenTelemetry span's traceparent and
    tracestate, or alone when there is no span. Scopes nest, and an inner one
    replaces the outer one; exiting restores the previous value. Ignored by
    endpoints whose policy lacks ``OUTGOING``.

    As for a received header, a value that isn't text is left out, and so is
    a ``traceparent`` that breaks W3C Trace Context §3.2, with its
    ``tracestate``. The ``baggage`` is read as a received one and written
    again, and the ``tracestate`` is cut as it is sent, so either one with no
    member that can be sent is left out too. When neither a ``traceparent`` nor a ``baggage`` is left,
    the scope changes nothing, and so does ``None`` (what
    `incoming_trace_context` returns when nothing arrived). The first time it leaves something out, a
    warning on the ``uipath_ipc.trace_context`` logger says why."""
    if trace_context is None:
        yield None
        return
    if not isinstance(trace_context, TraceContext):
        raise TypeError("trace_context must be a TraceContext or None")
    left_out: list[str] = []

    def text(name: str) -> str | None:
        value = getattr(trace_context, name)
        if value is not None and not isinstance(value, str):
            left_out.append(f"the {name} isn't text")
            return None
        return value

    traceparent = text(TRACEPARENT)
    if traceparent is not None and not _is_valid_traceparent(traceparent):
        left_out.append(f"the {TRACEPARENT} isn't a W3C traceparent")
        traceparent = None
    baggage = text(BAGGAGE)
    # The baggage is encoded once, here, as W3CPropagator would write what it reads.
    encoded_baggage = baggage and _encode_baggage(_decode_baggage(baggage))
    if baggage and not encoded_baggage:
        left_out.append(f"the {BAGGAGE} has no member it can send")
    baggage = encoded_baggage
    tracestate = text(TRACESTATE)
    if tracestate and traceparent is None:
        left_out.append(f"the {TRACESTATE} goes only with a {TRACEPARENT}")
        tracestate = None
    elif tracestate:
        # Cut once, here, as it is sent.
        cut = _validate_tracestate(tracestate)
        if not cut:
            left_out.append(f"the {TRACESTATE} has no member it can send")
        tracestate = cut
    global _left_out_logged
    if left_out and not _left_out_logged:
        _left_out_logged = True
        _logger.warning(
            "A use_trace_context scope left out what it couldn't use: %s.",
            ", ".join(left_out),
        )
    if traceparent is None and not baggage:
        yield trace_context
        return
    token = _override.set(
        TraceContext(traceparent, tracestate, baggage)
    )
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
        baggage = tc and tc.baggage
    else:
        # Encoded when the scope opened.
        baggage = tc.baggage
        if tc.traceparent is None:
            # Without a traceparent, the scope's baggage goes with the span's trace.
            ambient = _from_opentelemetry()
            if ambient is not None and ambient.traceparent:
                tc = ambient
    if tc is None:
        return None
    headers: dict[str, str] = {}
    if tc.traceparent and _is_valid_traceparent(tc.traceparent):
        # A higher version goes as the version 00 it reads as.
        headers[TRACEPARENT] = "00" + tc.traceparent[2:55]
        tracestate = tc.tracestate and _validate_tracestate(tc.tracestate)
        if tracestate:
            headers[TRACESTATE] = tracestate
    if baggage:
        headers[BAGGAGE] = baggage
    return headers or None


def from_headers(headers: Mapping[str, str] | None) -> TraceContext | None:
    """Reads the exact lowercase names. A traceparent that breaks W3C Trace
    Context is ignored, and so is its tracestate (§3.3); the tracestate is cut
    before its first invalid member, and the baggage is kept as received.
    ``None`` when neither a traceparent nor a baggage is left."""
    if not isinstance(headers, Mapping):
        return None
    traceparent = _get_header(headers, TRACEPARENT)
    if traceparent is not None and not _is_valid_traceparent(traceparent):
        traceparent = None
    received_tracestate = _get_header(headers, TRACESTATE) if traceparent else None
    tracestate = _validate_tracestate(received_tracestate) if received_tracestate else None
    baggage = _get_header(headers, BAGGAGE)
    if not traceparent and not baggage:
        return None
    return TraceContext(traceparent, tracestate, baggage)


@contextlib.contextmanager
def incoming_scope(trace_context: TraceContext | None, enabled: bool) -> Iterator[None]:
    """Run an incoming handler under `trace_context`.

    The receive loop starts with an empty context, and this resets the
    override and incoming values anyway, so none seen by whoever opened the
    connection can leak into the handler. The OpenTelemetry context is replaced too: the
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
            _opentelemetry().context.detach(otel_token)
        _incoming_tracestate.reset(tracestate_token)
        _incoming.reset(incoming_token)
        _override.reset(override_token)


# --- The headers, as W3CPropagator reads and writes them ------------------------


def _is_valid_traceparent(value: str) -> bool:
    if _TRACEPARENT.match(value) is None:
        return False
    return len(value) == 55 or (value[:2] != "00" and value[55] == "-")


def _validate_tracestate(tracestate: str) -> str | None:
    """The raw text up to the end of the last valid member before the first
    invalid one, within 256 characters."""
    processed = 0
    while processed < len(tracestate):
        comma = tracestate.find(",", processed)
        entry = tracestate[processed : len(tracestate) if comma < 0 else comma]
        delta = len(entry) + (comma >= 0)
        if processed + delta > _MAX_TRACESTATE_LENGTH:
            break
        # On the raw text, before trimming: '=' neither first nor last.
        equal = entry.find("=")
        if equal <= 0 or equal == len(entry) - 1:
            break
        if not _TRACESTATE_KEY.fullmatch(entry[:equal].strip(" \t")):
            break
        if not _TRACESTATE_VALUE.fullmatch(entry[equal + 1 :].strip(" ")):
            break
        processed += delta
    return tracestate[:processed].removesuffix(",") or None


def _get_header(headers: Mapping[str, str], name: str) -> str | None:
    value = headers.get(name)
    # A value that isn't a string, from a peer that ignores the wire format, is ignored.
    return value if isinstance(value, str) else None


def _encode_baggage(entries: Mapping[str, object]) -> str | None:
    """``key = value`` members joined by ``, ``, in map order: at most 64, and
    stopping at the first that, with its ``, ``, takes the text to 8192
    characters. A member whose trimmed key isn't a token is skipped."""
    encoded = ""
    good_length = count = 0
    for key, value in entries.items():
        if count == _MAX_BAGGAGE_MEMBERS or len(encoded) >= _MAX_BAGGAGE_LENGTH:
            break
        key = key.strip(" \t") if isinstance(key, str) else ""
        if not _BAGGAGE_KEY.fullmatch(key):
            continue
        try:
            text = "" if value is None else str(value)
        except Exception:
            # So is one str() can't write: OpenTelemetry takes any value.
            continue
        encoded += f"{key} = {_escape(text)}, "
        count += 1
        if len(encoded) < _MAX_BAGGAGE_LENGTH:
            good_length = len(encoded)
    return encoded[: good_length - 2] if good_length > 2 else None


def _escape(text: str) -> str:
    return quote(_well_formed(text.strip(" \t")), safe=_BAGGAGE_VALUE_SAFE)


def _well_formed(text: str) -> str:
    # The UTF-16 round trip turns a lone surrogate into U+FFFD and a pair into
    # its character, as .NET and JS send them. As is, a surrogate would make
    # quote raise.
    return text.encode("utf-16-le", "surrogatepass").decode("utf-16-le", "replace")


def _decode_baggage(baggage: str) -> dict[str, str]:
    """Stops at a member whose raw text has no '=', or has it first or last;
    skips one whose trimmed key isn't a token, or whose value has a non-ASCII
    character or a '%' without two hex digits. Keys aren't decoded, properties
    (after ';') stay in the value, and the first of a repeated key wins."""
    entries: dict[str, str] = {}
    for member in baggage.split(","):
        equal = member.find("=")
        if equal <= 0 or equal == len(member) - 1:
            break
        key = member[:equal].strip(" \t")
        value = member[equal + 1 :].strip(" \t")
        if not _BAGGAGE_KEY.fullmatch(key) or not value.isascii() or _BAD_ESCAPE.search(value):
            continue
        # A percent-escape that isn't valid UTF-8 decodes to U+FFFD.
        entries.setdefault(key, unquote(value, errors="replace"))
    return entries


# --- OpenTelemetry (optional) --------------------------------------------------


# OpenTelemetry's modules, imported on first use: None until then, False when
# they can't be. Python doesn't remember a failed import, and retrying it would
# cost every call.
_otel: Any = None


def _opentelemetry() -> Any:
    global _otel
    if _otel is None:
        try:
            from opentelemetry import baggage, trace
            from opentelemetry import context as otel_context
            from opentelemetry.trace.propagation.tracecontext import (
                TraceContextTextMapPropagator,
            )
        except Exception:
            _otel = False
        else:
            _otel = SimpleNamespace(
                baggage=baggage,
                trace=trace,
                context=otel_context,
                propagator=TraceContextTextMapPropagator(),
            )
    return _otel or None


_logger = logging.getLogger(__name__)
_read_failure_logged = False
_left_out_logged = False


def _from_opentelemetry() -> TraceContext | None:
    """The current span and baggage, the baggage already encoded."""
    otel = _opentelemetry()
    if otel is None:
        return None
    # OpenTelemetry keeps what it's given unchecked. As its own propagators do,
    # each part goes on its own: a part that can't be read is left out, without
    # failing the call, and the first time that happens, it says why. A baggage
    # value whose str() raises is skipped by _encode_baggage.
    encoded_baggage = _read(lambda: _encode_baggage(otel.baggage.get_all()))
    span_context = _read(lambda: otel.trace.get_current_span().get_span_context())
    if span_context is None or not _read(lambda: span_context.is_valid):
        return TraceContext(baggage=encoded_baggage) if encoded_baggage else None
    traceparent = _read(
        lambda: f"00-{span_context.trace_id:032x}-{span_context.span_id:016x}-"
        f"{int(span_context.trace_flags or 0):02x}"
    )
    if traceparent is None:
        return TraceContext(baggage=encoded_baggage) if encoded_baggage else None
    return TraceContext(traceparent, _read(lambda: _tracestate(span_context)), encoded_baggage)


def _tracestate(span_context: Any) -> str | None:
    received = _incoming_tracestate.get()
    if (
        received is not None
        and received[0] == span_context.trace_id
        and received[1] == span_context.trace_state
    ):
        # Unchanged since receipt (child spans inherit it): forward it verbatim,
        # like .NET, rather than OpenTelemetry's filtered re-serialization.
        return received[2]
    tracestate = span_context.trace_state.to_header()
    # A TraceState subclass may write something that isn't text.
    if tracestate and not isinstance(tracestate, str):
        raise TypeError(f"the TraceState wrote a {type(tracestate).__name__}, not text")
    return tracestate or None


def _read(part: Any) -> Any:
    try:
        return part()
    except Exception:
        global _read_failure_logged
        if not _read_failure_logged:
            _read_failure_logged = True
            _logger.warning(
                "Part of the OpenTelemetry context couldn't be read, so it isn't sent.",
                exc_info=True,
            )
        return None


def _attach_opentelemetry(trace_context: TraceContext | None) -> object | None:
    otel = _opentelemetry()
    if otel is None:
        return None
    ctx = otel.context.Context()
    if trace_context is not None and trace_context.traceparent:
        carrier = {TRACEPARENT: trace_context.traceparent}
        if trace_context.tracestate:
            carrier[TRACESTATE] = trace_context.tracestate
        ctx = otel.propagator.extract(carrier, context=ctx)
        span_context = otel.trace.get_current_span(ctx).get_span_context()
        if span_context.is_valid and trace_context.tracestate:
            _incoming_tracestate.set(
                (span_context.trace_id, span_context.trace_state, trace_context.tracestate)
            )
    if trace_context is not None and trace_context.baggage:
        for key, value in _decode_baggage(trace_context.baggage).items():
            ctx = otel.baggage.set_baggage(key, value, ctx)
    return otel.context.attach(ctx)
