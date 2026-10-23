"""Unit tests for W3C trace-context validation, encoding and precedence."""

from __future__ import annotations

import asyncio

import pytest

from uipath_ipc import TraceContext, incoming_trace_context, use_trace_context
from uipath_ipc.trace_context import (
    InvalidTraceContextError,
    _decode_baggage,
    _encode_baggage,
    from_headers,
    incoming_scope,
    outgoing_headers,
)

TP = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
TP2 = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00"


# --- TraceContext -------------------------------------------------------------

def test_trace_context_normalizes_empty_optionals() -> None:
    tc = TraceContext(TP, "", "")
    assert (tc.traceparent, tc.tracestate, tc.baggage) == (TP, None, None)


def test_repr_prints_traceparent_only() -> None:
    text = repr(TraceContext(TP, "congo=t61rcWkgMzE", "userId=alice"))
    assert TP in text
    assert "congo" not in text
    assert "alice" not in text


# --- use_trace_context --------------------------------------------------------

def _use(tc: TraceContext) -> None:
    with use_trace_context(tc):
        pass


def test_use_requires_a_trace_context() -> None:
    with pytest.raises(TypeError):
        _use(None)  # type: ignore[arg-type]


def test_use_rejects_too_many_tracestate_members() -> None:
    _use(TraceContext(TP, ",".join(f"k{i}=v" for i in range(32))))
    with pytest.raises(InvalidTraceContextError, match="tracestate.*32"):
        _use(TraceContext(TP, ",".join(f"k{i}=v" for i in range(33))))


def test_use_rejects_baggage_over_the_limits() -> None:
    _use(TraceContext(TP, baggage=",".join(f"k{i}=v" for i in range(64))))
    with pytest.raises(InvalidTraceContextError, match="baggage.*64"):
        _use(TraceContext(TP, baggage=",".join(f"k{i}=v" for i in range(65))))
    _use(TraceContext(TP, baggage="k=" + "v" * 8190))
    with pytest.raises(InvalidTraceContextError, match="baggage.*8192"):
        _use(TraceContext(TP, baggage="k=" + "v" * 8191))
    # UTF-8 bytes, not characters: 4096 two-byte chars + "k=" = 8194 bytes.
    with pytest.raises(ValueError, match="8192"):
        _use(TraceContext(TP, baggage="k=" + "é" * 4096))


def test_lone_surrogate_baggage_is_counted_not_raised() -> None:
    _use(TraceContext(TP, baggage="k=\ud800"))
    with pytest.raises(InvalidTraceContextError, match="8192"):
        _use(TraceContext(TP, baggage="k=" + "\ud800" * 2731))


def test_no_ambient_context_sends_nothing() -> None:
    assert outgoing_headers() is None


def test_override_scopes_nest_and_restore() -> None:
    outer, inner = TraceContext(TP, "a=1", "x=1"), TraceContext(TP2)
    with use_trace_context(outer):
        assert outgoing_headers() == {
            "traceparent": TP, "tracestate": "a=1", "baggage": "x=1",
        }
        with use_trace_context(inner):
            assert outgoing_headers() == {"traceparent": TP2}
        assert outgoing_headers()["traceparent"] == TP  # type: ignore[index]
    assert outgoing_headers() is None


async def test_override_flows_into_tasks_created_inside() -> None:
    async def child() -> dict[str, str] | None:
        await asyncio.sleep(0)
        return outgoing_headers()

    with use_trace_context(TraceContext(TP)):
        task = asyncio.create_task(child())
    assert outgoing_headers() is None
    assert await task == {"traceparent": TP}


# --- incoming headers ---------------------------------------------------------

def test_from_headers_without_traceparent_is_ignored() -> None:
    assert from_headers(None) is None
    assert from_headers({}) is None
    assert from_headers({"tracestate": "x" * 10, "baggage": ",".join(["a=b"] * 100)}) is None
    assert from_headers({"traceparent": None}) is None  # type: ignore[dict-item]


def test_from_headers_is_case_insensitive_and_ignores_unknown_keys() -> None:
    tc = from_headers({"TraceParent": TP, "TRACESTATE": "a=1", "Baggage": "x=1", "future": "?"})
    assert tc == TraceContext(TP, "a=1", "x=1")


@pytest.mark.parametrize(
    "headers",
    [
        {"traceparent": TP, "tracestate": ",".join(f"k{i}=v" for i in range(33))},
        {"traceparent": TP, "baggage": ",".join(f"k{i}=v" for i in range(65))},
        {"traceparent": TP, "baggage": "k=" + "v" * 8191},
    ],
    ids=["over-32-tracestate-members", "over-64-baggage-members", "over-8192-baggage-bytes"],
)
def test_from_headers_takes_values_over_the_limits_as_they_are(headers: dict[str, str]) -> None:
    tc = from_headers(headers)
    assert tc == TraceContext(TP, headers.get("tracestate"), headers.get("baggage"))


def test_incoming_scope_exposes_the_values_and_hides_the_override() -> None:
    received = TraceContext(TP2)
    with use_trace_context(TraceContext(TP)):
        with incoming_scope(received, enabled=False):
            assert incoming_trace_context() is received
            # Received values never become the outgoing override.
            assert outgoing_headers() is None
        assert outgoing_headers() == {"traceparent": TP}
    assert incoming_trace_context() is None


# --- baggage encoding -----------------------------------------------------------

def test_baggage_encoding_matches_dotnet_uri_escape_data_string() -> None:
    assert _encode_baggage({}) is None
    assert _encode_baggage({"userId": "alice", "x": "a b,c=d", "k~-_.": "é"}) == (
        "userId=alice,x=a%20b%2Cc%3Dd,k~-_.=%C3%A9"
    )


def test_baggage_with_encoded_separators_round_trips() -> None:
    encoded = "k%3B%3D%2C=a%3Bb%3Dc%2Cd"
    assert _encode_baggage({"k;=,": "a;b=c,d"}) == encoded
    assert _decode_baggage(encoded) == [("k;=,", "a;b=c,d")]


@pytest.mark.parametrize(
    ("baggage", "decoded"),
    [
        ("k=50%,x=%zz%41,y=%FF%C3%A9", [("k", "50%"), ("x", "%zzA"), ("y", "%FFé")]),
        ("a=%C3%A9%A9,b=%E2%82%C3%A9", [("a", "é%A9"), ("b", "%E2%82é")]),
        ("%c3%a9=%C0%80%ED%A0%80%F4%90%80%80", [("é", "%C0%80%ED%A0%80%F4%90%80%80")]),
    ],
    ids=["stray-and-invalid", "truncated-sequences", "overlong-surrogate-out-of-range"],
)
def test_baggage_decoding_keeps_escapes_that_are_not_utf8_like_dotnet(
    baggage: str, decoded: list[tuple[str, str]]
) -> None:
    assert _decode_baggage(baggage) == decoded


def test_baggage_member_without_equals_raises() -> None:
    with pytest.raises(ValueError):
        _decode_baggage("k=v,novalue")


# --- OpenTelemetry precedence -------------------------------------------------------

def _otel_scope(traceparent: str, tracestate: str | None = None, **entries: str):
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage
    from opentelemetry import context as otel_context

    span_context = trace.SpanContext(
        trace_id=int(traceparent[3:35], 16),
        span_id=int(traceparent[36:52], 16),
        is_remote=False,
        trace_flags=trace.TraceFlags(int(traceparent[53:], 16)),
        trace_state=trace.TraceState.from_header([tracestate]) if tracestate else None,
    )
    ctx = trace.set_span_in_context(trace.NonRecordingSpan(span_context))
    for key, value in entries.items():
        ctx = baggage.set_baggage(key, value, ctx)

    class _Scope:
        def __enter__(self) -> None:
            self._token = otel_context.attach(ctx)

        def __exit__(self, *exc: object) -> None:
            otel_context.detach(self._token)

    return _Scope()


def test_opentelemetry_failing_to_import_counts_as_not_installed(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    import builtins

    real_import = builtins.__import__

    def failing_import(name: str, *args: object, **kwargs: object) -> object:
        if name.startswith("opentelemetry"):
            raise RuntimeError("broken install")
        return real_import(name, *args, **kwargs)  # type: ignore[arg-type]

    monkeypatch.setattr(builtins, "__import__", failing_import)
    received = TraceContext(TP)
    with incoming_scope(received, enabled=True):
        assert incoming_trace_context() is received
        assert outgoing_headers() is None


def test_opentelemetry_span_is_propagated() -> None:
    with _otel_scope(TP, "congo=t61rcWkgMzE", userId="alice", x="a b"):
        assert outgoing_headers() == {
            "traceparent": TP,
            "tracestate": "congo=t61rcWkgMzE",
            "baggage": "userId=alice,x=a%20b",
        }
    assert outgoing_headers() is None


def test_explicit_override_wins_over_opentelemetry() -> None:
    with _otel_scope(TP, userId="alice"):
        with use_trace_context(TraceContext(TP2)):
            assert outgoing_headers() == {"traceparent": TP2}


@pytest.mark.parametrize(
    "entries",
    [{"big": "v" * 9000}, {f"k{i}": "v" for i in range(65)}],
    ids=["over-8192-bytes", "over-64-members"],
)
def test_opentelemetry_baggage_over_the_w3c_limits_is_sent_as_is(entries: dict[str, str]) -> None:
    # OpenTelemetry's TraceState already refuses more than 32 members.
    expected = ",".join(f"{key}={value}" for key, value in entries.items())
    with _otel_scope(TP, **entries):
        headers = outgoing_headers()
    assert headers == {"traceparent": TP, "baggage": expected}
    assert from_headers(headers) == TraceContext(TP, baggage=expected)


def test_incoming_scope_sets_the_remote_parent_and_baggage() -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage

    received = TraceContext(TP, "congo=t61rcWkgMzE", "userId=alice,x=a%20b")
    with _otel_scope(TP2, leaked="yes"):
        with incoming_scope(received, enabled=True):
            sc = trace.get_current_span().get_span_context()
            assert (sc.trace_id, sc.span_id) == (int(TP[3:35], 16), int(TP[36:52], 16))
            assert sc.is_remote
            assert int(sc.trace_flags) == 1
            assert sc.trace_state.to_header() == "congo=t61rcWkgMzE"
            assert dict(baggage.get_all()) == {"userId": "alice", "x": "a b"}
            # A nested call propagates the same trace.
            assert outgoing_headers()["traceparent"] == TP  # type: ignore[index]
        with incoming_scope(None, enabled=True):
            assert not trace.get_current_span().get_span_context().is_valid
            assert dict(baggage.get_all()) == {}
        assert dict(baggage.get_all()) == {"leaked": "yes"}


@pytest.mark.parametrize(
    "traceparent",
    [
        "garbage",
        TP.upper(),
        "00-00000000000000000000000000000000-00f067aa0ba902b7-01",
        "ff" + TP[2:],
        TP + "-extra",
    ],
    ids=["garbage", "uppercase", "zero-trace-id", "version-ff", "version-00-with-extra"],
)
def test_incoming_scope_gives_no_remote_parent_for_a_traceparent_opentelemetry_rejects(
    traceparent: str,
) -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage

    received = TraceContext(traceparent, "congo=t61rcWkgMzE", "k=v")
    with _otel_scope(TP2, leaked="yes"):
        with incoming_scope(received, enabled=True):
            assert incoming_trace_context() is received
            assert not trace.get_current_span().get_span_context().is_valid
            assert dict(baggage.get_all()) == {"k": "v"}
            # No span context, so a nested call sends nothing, not even the tracestate.
            assert outgoing_headers() is None


@pytest.mark.parametrize(
    ("tracestate", "parsed"),
    [
        ("congo=t61rcWkgMzE, rojo=00f0", "congo=t61rcWkgMzE,rojo=00f0"),
        ("Congo=t61,rojo=00f0", ""),
        ("congo=t61rcWkgMzE,Bad Member", ""),
        ("congo=t61,congo=x", ""),
        ("Bad", ""),
        (",".join(f"k{i}={'v' * 256}" for i in range(32)), ""),
    ],
    ids=["whitespace", "uppercase-key", "bad-member", "duplicate-key", "bad", "over-8192-chars"],
)
def test_received_tracestate_is_forwarded_verbatim(tracestate: str, parsed: str) -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import context as otel_context

    def outgoing_tracestate_under(span_context: object) -> str | None:
        token = otel_context.attach(trace.set_span_in_context(trace.NonRecordingSpan(span_context)))
        try:
            return outgoing_headers().get("tracestate")  # type: ignore[union-attr]
        finally:
            otel_context.detach(token)

    with incoming_scope(from_headers({"traceparent": TP, "tracestate": tracestate}), enabled=True):
        parent = trace.get_current_span().get_span_context()
        assert parent.trace_state.to_header() == parsed
        assert outgoing_headers()["tracestate"] == tracestate  # type: ignore[index]
        # A child span inherits the TraceState unchanged.
        child = trace.SpanContext(parent.trace_id, 0x1234, False, parent.trace_flags, parent.trace_state)
        assert outgoing_tracestate_under(child) == tracestate
        changed = trace.SpanContext(
            parent.trace_id, 0x1234, False, parent.trace_flags, trace.TraceState([("other", "1")])
        )
        assert outgoing_tracestate_under(changed) == "other=1"
