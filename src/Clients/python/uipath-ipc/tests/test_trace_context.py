"""Unit tests for W3C trace-context validation, encoding and precedence."""

from __future__ import annotations

import asyncio

import pytest

from uipath_ipc import TraceContext, incoming_trace_context, use_trace_context
from uipath_ipc.trace_context import (
    _decode_baggage,
    _encode_baggage,
    from_headers,
    incoming_scope,
    outgoing_headers,
)
from uipath_ipc.wire import Request

TP = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
TP2 = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00"
# Built with chr(), so that no escape for them sits in the source.
LONE_HIGH, LONE_LOW = chr(0xD800), chr(0xDE00)
PAIR = chr(0xD83D) + chr(0xDE00)
REPLACEMENT, BOM, GRINNING = chr(0xFFFD), chr(0xFEFF), chr(0x1F600)

INVALID_TRACEPARENTS = pytest.mark.parametrize(
    "traceparent",
    [
        "garbage",
        TP[:54],
        TP.upper(),
        "00-00000000000000000000000000000000-00f067aa0ba902b7-01",
        "00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01",
        "ff" + TP[2:],
        TP + "-extra",
        "01" + TP[2:] + "extra",
    ],
    ids=[
        "garbage", "too-short", "uppercase", "zero-trace-id", "zero-span-id", "version-ff",
        "version-00-with-extra", "higher-version-without-dash",
    ],
)


# --- TraceContext -------------------------------------------------------------

def test_trace_context_normalizes_empty_optionals() -> None:
    tc = TraceContext(TP, "", "")
    assert (tc.traceparent, tc.tracestate, tc.baggage) == (TP, None, None)
    assert TraceContext("", baggage="k=v").traceparent is None


def test_repr_prints_traceparent_only() -> None:
    text = repr(TraceContext(TP, "congo=t61rcWkgMzE", "userId=alice"))
    assert TP in text
    assert "congo" not in text
    assert "alice" not in text


# --- use_trace_context --------------------------------------------------------

def _use(tc: TraceContext) -> None:
    with use_trace_context(tc):
        pass


def test_forwarding_incoming_trace_context_sends_it_on_and_changes_nothing_when_none_arrived(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # Another ambient trace, faked so this runs without OpenTelemetry too: a
    # forward that did nothing would send it instead of the received one.
    import uipath_ipc.trace_context as trace_context_module

    ambient = TraceContext(TP2, "congo=t61rcWkgMzE", "ambient = 1")
    monkeypatch.setattr(trace_context_module, "_from_opentelemetry", lambda: ambient)

    def forwarded(received: TraceContext | None) -> dict[str, str] | None:
        with incoming_scope(received, enabled=True):
            with use_trace_context(incoming_trace_context()):
                return outgoing_headers()

    assert forwarded(None) == {"traceparent": TP2, "tracestate": "congo=t61rcWkgMzE", "baggage": "ambient = 1"}
    assert forwarded(from_headers({"traceparent": TP, "tracestate": "a=b", "baggage": "x=a%20b"})) == {
        "traceparent": TP,
        "tracestate": "a=b",
        "baggage": "x = a%20b",
    }


def test_use_rejects_what_is_not_a_trace_context() -> None:
    with pytest.raises(TypeError):
        _use("x")  # type: ignore[arg-type]


def _sent(tc: TraceContext | None) -> dict[str, str] | None:
    with use_trace_context(tc):
        return outgoing_headers()


@INVALID_TRACEPARENTS
def test_use_leaves_out_an_invalid_traceparent_and_its_tracestate(traceparent: str) -> None:
    assert _sent(TraceContext(traceparent, "a=b")) is None
    assert _sent(TraceContext(traceparent, "a=b", "k=v")) == {"baggage": "k = v"}


def test_use_says_once_what_it_leaves_out(
    monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
) -> None:
    import uipath_ipc.trace_context as module

    monkeypatch.setattr(module, "_left_out_logged", False)
    with caplog.at_level("WARNING", logger="uipath_ipc.trace_context"):
        _sent(TraceContext(TP))
        assert caplog.messages == []
        _sent(TraceContext(baggage="garbage"))
        _sent(TraceContext(baggage="garbage"))
        assert caplog.messages == [
            "A use_trace_context scope left out what it couldn't use: "
            "the baggage has no member it can send."
        ]
        caplog.clear()
        monkeypatch.setattr(module, "_left_out_logged", False)
        _sent(TraceContext(tracestate="a=b"))
        assert caplog.messages == [
            "A use_trace_context scope left out what it couldn't use: "
            "the tracestate goes only with a traceparent."
        ]
        caplog.clear()
        monkeypatch.setattr(module, "_left_out_logged", False)
        assert _sent(TraceContext(TP, "Bad=1,a=1")) == {"traceparent": TP}
        assert caplog.messages == [
            "A use_trace_context scope left out what it couldn't use: "
            "the tracestate has no member it can send."
        ]
        caplog.clear()
        monkeypatch.setattr(module, "_left_out_logged", False)
        _sent(TraceContext("garbage", baggage=5))  # type: ignore[arg-type]
        _sent(TraceContext("garbage"))
    assert caplog.messages == [
        "A use_trace_context scope left out what it couldn't use: "
        "the traceparent isn't a W3C traceparent, the baggage isn't text."
    ]


def test_use_leaves_out_a_value_that_is_not_text() -> None:
    assert _sent(TraceContext(baggage=5)) is None  # type: ignore[arg-type]
    assert _sent(TraceContext(TP, 7)) == {"traceparent": TP}  # type: ignore[arg-type]
    assert _sent(TraceContext(1, baggage="k=v")) == {"baggage": "k = v"}  # type: ignore[arg-type]


@pytest.mark.parametrize(
    "tc",
    [TraceContext(), TraceContext("", ""), TraceContext(tracestate="a=b"), TraceContext(baggage="garbage"), None],
)
def test_use_with_neither_a_traceparent_nor_a_baggage_left_changes_nothing(tc: TraceContext | None) -> None:
    with use_trace_context(TraceContext(TP)):
        assert _sent(tc) == {"traceparent": TP}
    assert _sent(tc) is None


@pytest.mark.parametrize(
    ("tracestate", "sent"),
    [
        ("congo=t61rcWkgMzE,Bad=1,rojo=00f0", "congo=t61rcWkgMzE"),
        ("Bad=1,congo=t61rcWkgMzE", None),
        (" congo = t61 ,\trojo=00f0", " congo = t61 ,\trojo=00f0"),
        ("congo=1,rojo=x y", "congo=1"),
        ("congo=1,rojo=\t1", "congo=1"),
        ("congo=1,rojo=", "congo=1"),
        ("congo=1,rojo= ", "congo=1"),
        ("congo=1,=1", "congo=1"),
        ("congo=1,rojo==1", "congo=1"),
        ("congo=1,,rojo=1", "congo=1"),
        ("congo=1,", "congo=1"),
        ("congo=1,congo=2", "congo=1,congo=2"),
        ("1@vendor/x_y-z*=!~", "1@vendor/x_y-z*=!~"),
        (",".join(f"k{i}=v" for i in range(40)), ",".join(f"k{i}=v" for i in range(40))),
        ("a=" + "v" * 254, "a=" + "v" * 254),
        ("a=" + "v" * 255, None),
        ("a=1,b=" + "v" * 250, "a=1,b=" + "v" * 250),
        ("a=1,b=" + "v" * 251, "a=1"),
    ],
    ids=[
        "cut-at-an-invalid-member", "invalid-first-member", "whitespace-kept",
        "space-inside-a-value", "tab-before-a-value", "equal-last", "equal-last-before-trim",
        "equal-first", "equal-in-the-value", "empty-member", "trailing-comma", "repeated-key",
        "vendor-key", "no-member-cap", "256-chars", "257-chars", "256-chars-with-comma",
        "257-chars-with-comma",
    ],
)
def test_tracestate_is_sent_up_to_its_first_invalid_member_like_w3c_propagator(
    tracestate: str, sent: str | None
) -> None:
    expected = {"traceparent": TP, "tracestate": sent} if sent else {"traceparent": TP}
    assert _sent(TraceContext(TP, tracestate)) == expected


def test_baggage_over_the_limits_like_w3c_propagator() -> None:
    assert _sent(TraceContext(TP, baggage=",".join(f"k{i}=v" for i in range(65)))) == {
        "traceparent": TP, "baggage": ", ".join(f"k{i} = v" for i in range(64)),
    }
    # "k = " + value + ", " must stay under 8192 characters.
    assert _sent(TraceContext(TP, baggage="k=" + "v" * 8185)) == {
        "traceparent": TP, "baggage": "k = " + "v" * 8185,
    }
    assert _sent(TraceContext(TP, baggage="k=" + "v" * 8186)) == {"traceparent": TP}
    # Sending stops at the member that is too long; the ones after it don't go either.
    assert _sent(TraceContext(TP, baggage="a=1,k=" + "v" * 9000 + ",b=2")) == {
        "traceparent": TP, "baggage": "a = 1",
    }
    # Characters as escaped: "k = " and 2728 three-character escapes and ", " make 8190.
    assert _sent(TraceContext(TP, baggage="k=" + "%25" * 2728)) == {
        "traceparent": TP, "baggage": "k = " + "%25" * 2728,
    }
    assert _sent(TraceContext(TP, baggage="k=" + "%25" * 2729)) == {"traceparent": TP}


@pytest.mark.parametrize(
    ("baggage", "sent"),
    [
        ("userId=alice,x=a%20b", "userId = alice, x = a%20b"),
        (" k = v ", "k = v"),
        ("a=1,foo,b=2", "a = 1"),
        ("foo,a=1", None),
        ("a=1,b=,c=3", "a = 1"),
        ("a=1,b= ,c=3", "a = 1, b = , c = 3"),
        ("a=1,Bad Key=2,b=2", "a = 1, b = 2"),
        ("a=50%,b=2", "b = 2"),
        ("a=café,b=2", "b = 2"),
        ("k=v;p=1", "k = v%3Bp=1"),
        ("k%41=1", "k%41 = 1"),
        ("a=1,a=2", "a = 1"),
        ("k=%C3%A9", "k = %C3%A9"),
        ("k=%c3%a9", "k = %C3%A9"),
    ],
    ids=[
        "format", "trimmed", "stops-at-no-equal", "nothing-before-no-equal",
        "stops-at-equal-last", "equal-last-before-trim", "skips-a-non-token-key",
        "skips-a-broken-escape", "skips-raw-non-ascii", "properties-in-the-value",
        "key-not-decoded", "first-wins", "escape-kept", "escape-uppercased",
    ],
)
def test_an_override_baggage_is_sent_as_w3c_propagator_writes_what_it_reads(
    baggage: str, sent: str | None
) -> None:
    expected = {"traceparent": TP, "baggage": sent} if sent else {"traceparent": TP}
    assert _sent(TraceContext(TP, baggage=baggage)) == expected


def test_without_a_traceparent_the_baggage_travels_alone() -> None:
    assert _sent(TraceContext(tracestate="a=b", baggage="k=v")) == {"baggage": "k = v"}
    assert _sent(TraceContext(baggage="foo")) is None


def test_an_override_without_a_traceparent_takes_only_the_ambient_trace(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    # The ambient trace is faked, so this runs without OpenTelemetry too.
    import uipath_ipc.trace_context as trace_context_module

    monkeypatch.setattr(
        trace_context_module,
        "_from_opentelemetry",
        lambda: TraceContext(TP, "congo=t61rcWkgMzE", "ambient = 1"),
    )
    # The scope's baggage replaces the ambient one.
    assert _sent(TraceContext(tracestate="a=b", baggage="k=v")) == {
        "traceparent": TP, "tracestate": "congo=t61rcWkgMzE", "baggage": "k = v",
    }


def test_a_higher_version_traceparent_is_sent_as_version_00() -> None:
    assert _sent(TraceContext("01" + TP[2:] + "-future", "congo=1")) == {
        "traceparent": TP, "tracestate": "congo=1",
    }
    assert _sent(TraceContext("cc" + TP[2:])) == {"traceparent": TP}


def test_no_ambient_context_sends_nothing() -> None:
    assert outgoing_headers() is None


def test_override_scopes_nest_and_restore() -> None:
    outer, inner = TraceContext(TP, "a=1", "x=1"), TraceContext(TP2)
    with use_trace_context(outer):
        assert outgoing_headers() == {
            "traceparent": TP, "tracestate": "a=1", "baggage": "x = 1",
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

def test_from_headers_without_traceparent_keeps_only_the_baggage() -> None:
    assert from_headers(None) is None
    assert from_headers({}) is None
    assert from_headers({"tracestate": "a=b"}) is None
    assert from_headers({"traceparent": None}) is None  # type: ignore[dict-item]
    assert from_headers({"tracestate": "a=b", "baggage": "a=b"}) == TraceContext(baggage="a=b")


@INVALID_TRACEPARENTS
def test_from_headers_ignores_an_invalid_traceparent_with_its_tracestate(traceparent: str) -> None:
    assert from_headers({"traceparent": traceparent, "tracestate": "a=b"}) is None
    assert from_headers({"traceparent": traceparent, "tracestate": "a=b", "baggage": "k=v"}) == (
        TraceContext(baggage="k=v")
    )


def test_from_headers_accepts_a_higher_version_traceparent() -> None:
    for future in ("01" + TP[2:] + "-future", "01" + TP[2:] + "-é", "01" + TP[2:]):
        assert from_headers({"traceparent": future}) == TraceContext(future)


def test_from_headers_cuts_the_tracestate_at_its_first_invalid_member() -> None:
    assert from_headers(
        {"traceparent": TP, "tracestate": " congo=t61rcWkgMzE ,Bad=1,rojo=00f0"}
    ) == TraceContext(TP, " congo=t61rcWkgMzE ")
    assert from_headers({"traceparent": TP, "tracestate": "2020-01-01T00:00:00Z"}) == TraceContext(TP)


def test_from_headers_reads_only_the_exact_lowercase_names() -> None:
    assert from_headers({"TraceParent": TP, "TRACESTATE": "a=1", "Baggage": "x=1"}) is None
    assert from_headers({"traceparent": TP, "Tracestate": "a=1", "future": "?"}) == TraceContext(TP)
    assert from_headers({"Traceparent": TP, "traceparent": None}) is None  # type: ignore[dict-item]


def test_from_headers_keeps_the_values_without_limits() -> None:
    tracestate = ",".join(f"k{i}=v" for i in range(33))
    baggage = ",".join(f"k{i}=v" for i in range(65)) + ",big=" + "v" * 9000 + ",foo,=x"
    assert from_headers({"traceparent": TP, "tracestate": tracestate, "baggage": baggage}) == (
        TraceContext(TP, tracestate, baggage)
    )
    assert from_headers({"baggage": "foo,=x"}) == TraceContext(baggage="foo,=x")


@pytest.mark.parametrize(
    ("headers", "received"),
    [
        (f'{{"traceparent":null,"TraceParent":"{TP}"}}', None),
        (f'{{"traceparent":5}}', None),
        (f'{{"traceparent":"{TP}","traceparent":5}}', None),
        (f'{{"traceparent":"{TP}"}},"Headers":5', None),
        (f'{{"traceparent":"{TP}"}},"Headers":null', None),
        (f'{{}},"headers":{{"traceparent":"{TP}"}}', None),
        (f'{{"traceparent":"{TP}"}},"headers":{{}}', TraceContext(TP)),
        (f'{{"traceparent":"{TP}"}},"HEADERS":5', TraceContext(TP)),
    ],
    ids=[
        "null-exact-name", "number-exact-name", "repeated-name",
        "repeated-headers-number", "repeated-headers-null", "lowercase-headers-not-read",
        "lowercase-headers-not-overriding", "uppercase-headers-not-overriding",
    ],
)
def test_from_headers_reads_a_request_like_dotnet_and_js(
    headers: str, received: TraceContext | None
) -> None:
    # Parsed as a request arrives, so only "Headers" is read. The repeated name
    # and the repeated Headers only pin json.loads, which keeps the last.
    request = Request.from_json(
        '{"Endpoint":"E","MethodName":"M","Parameters":[],"Id":"1","TimeoutInSeconds":0,'
        f'"Headers":{headers}}}'
    )
    assert from_headers(request.headers) == received


def test_from_headers_ignores_values_that_are_not_strings() -> None:
    assert from_headers({"traceparent": TP, "tracestate": 42, "baggage": ["k=v"]}) == TraceContext(TP)  # type: ignore[dict-item]
    assert from_headers(["traceparent", TP]) is None  # type: ignore[arg-type]


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

def test_baggage_encoding_matches_w3c_propagator() -> None:
    assert _encode_baggage({}) is None
    assert _encode_baggage({"userId": "alice", "x": " a b,c=d\t", "k~-_.": "é"}) == (
        "userId = alice, x = a%20b%2Cc=d, k~-_. = %C3%A9"
    )


def test_baggage_encoding_escapes_what_isnt_a_baggage_octet_and_percent() -> None:
    ascii_text = "".join(chr(c) for c in range(0x20, 0x7F))
    assert _encode_baggage({"k": f"x{ascii_text}x", "c": "\x01\x7f"}) == (
        "k = x%20!%22#$%25&'()*+%2C-./0123456789:%3B<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[%5C]^_`"
        "abcdefghijklmnopqrstuvwxyz{|}~x, c = %01%7F"
    )


def test_baggage_encoding_sends_a_lone_surrogate_as_u_fffd_like_dotnet_and_js() -> None:
    assert _encode_baggage(
        {"k": f"a{LONE_HIGH}b", "pair": PAIR, "reversed": LONE_LOW + chr(0xD83D)}
    ) == "k = a%EF%BF%BDb, pair = %F0%9F%98%80, reversed = %EF%BF%BD%EF%BF%BD"


def test_baggage_encoding_sends_a_token_key_trimmed_and_raw() -> None:
    assert _encode_baggage(
        {None: "x", "": "x", "user id": "x", "k" + LONE_HIGH: "x", "é": "x", " k%41\t": "v", "!#$%&'*+-.^_`|~": "1"}  # type: ignore[dict-item]
    ) == "k%41 = v, !#$%&'*+-.^_`|~ = 1"


def test_baggage_encoding_sends_a_none_or_blank_value_as_empty() -> None:
    assert _encode_baggage({"empty": None, "blank": " \t", "x": "1"}) == "empty = , blank = , x = 1"


def test_baggage_encoding_skips_non_token_keys_without_counting_them() -> None:
    entries: dict[str, str] = {"bad key": "x"}
    entries.update({f"k{i}": "v" for i in range(65)})
    assert _encode_baggage(entries) == ", ".join(f"k{i} = v" for i in range(64))


def test_baggage_with_encoded_separators_round_trips() -> None:
    encoded = "k = a%3Bb=c%2Cd%25"
    assert _encode_baggage({"k": "a;b=c,d%"}) == encoded
    assert _decode_baggage(encoded) == {"k": "a;b=c,d%"}


@pytest.mark.parametrize(
    ("escaped", "decoded"),
    [
        ("%FF%C3%A9", REPLACEMENT + "é"),
        ("%c3%a9", "é"),
        ("%C3%28", REPLACEMENT + "("),
        ("%C3%A9%A9", "é" + REPLACEMENT),
        ("%E2%82%C3%A9", REPLACEMENT + "é"),
        ("%C0%80", REPLACEMENT * 2),
        ("%ED%A0%80", REPLACEMENT * 3),
        ("%F4%90%80%80", REPLACEMENT * 4),
        ("%F0%9F%98%80", GRINNING),
        ("a+b", "a+b"),  # unquote, not unquote_plus
        ("a b", "a b"),
        ("%EF%BB%BFx", BOM + "x"),
        ("x%EF%BB%BFy", "x" + BOM + "y"),
    ],
)
def test_baggage_decoding_gives_u_fffd_for_invalid_utf8(escaped: str, decoded: str) -> None:
    assert _decode_baggage(f"k={escaped}") == {"k": decoded}


@pytest.mark.parametrize("value", ["50%", "%zz%41", "%4", "é", "a" + LONE_HIGH])
def test_baggage_decoding_skips_a_broken_escape_or_a_raw_non_ascii_value(value: str) -> None:
    assert _decode_baggage(f"a=1,k={value},b=2") == {"a": "1", "b": "2"}


def test_baggage_decoding_follows_w3c_propagator() -> None:
    assert _decode_baggage(" a = 1 ,b= ,c=3;ttl=30,Bad Key=4,k%41=5,a=6,d=7,") == {
        "a": "1", "b": "", "c": "3;ttl=30", "k%41": "5", "d": "7",
    }
    # It stops for good at a member with no '=', or with it first or last.
    for stop in ("foo", "", "=x", "k=", " k="):
        assert _decode_baggage(f"a=1,{stop},b=2") == {"a": "1"}


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


def test_opentelemetry_failing_to_import_counts_as_not_installed_and_is_tried_once(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    import builtins

    import uipath_ipc.trace_context as trace_context_module

    real_import = builtins.__import__
    attempts: list[str] = []

    def failing_import(name: str, *args: object, **kwargs: object) -> object:
        if name.startswith("opentelemetry"):
            attempts.append(name)
            raise RuntimeError("broken install")
        return real_import(name, *args, **kwargs)  # type: ignore[arg-type]

    monkeypatch.setattr(trace_context_module, "_otel", None)
    monkeypatch.setattr(builtins, "__import__", failing_import)
    received = TraceContext(TP)
    for _ in range(3):
        with incoming_scope(received, enabled=True):
            assert incoming_trace_context() is received
            assert outgoing_headers() is None
    # Python doesn't remember a failed import: retrying it would cost every call.
    assert len(attempts) == 1


def test_opentelemetry_span_is_propagated() -> None:
    with _otel_scope(TP, "congo=t61rcWkgMzE", userId="alice", x="a b"):
        assert outgoing_headers() == {
            "traceparent": TP,
            "tracestate": "congo=t61rcWkgMzE",
            "baggage": "userId = alice, x = a%20b",
        }
    assert outgoing_headers() is None


def test_explicit_override_wins_over_opentelemetry() -> None:
    with _otel_scope(TP, userId="alice"):
        with use_trace_context(TraceContext(TP2)):
            assert outgoing_headers() == {"traceparent": TP2}


def test_an_override_without_a_traceparent_keeps_the_opentelemetry_trace() -> None:
    with _otel_scope(TP, "congo=t61rcWkgMzE", userId="alice"):
        assert _sent(TraceContext(tracestate="a=b", baggage="k=v")) == {
            "traceparent": TP, "tracestate": "congo=t61rcWkgMzE", "baggage": "k = v",
        }


def test_an_empty_opentelemetry_context_sends_nothing() -> None:
    otel_context = pytest.importorskip("opentelemetry.context")
    with _otel_scope(TP, "congo=t61rcWkgMzE", userId="alice"):
        token = otel_context.attach(otel_context.Context())
        try:
            assert outgoing_headers() is None
        finally:
            otel_context.detach(token)


@pytest.mark.parametrize(
    ("entries", "sent"),
    [
        ({"big": "v" * 9000}, {"traceparent": TP}),
        (
            {"a": "1", "big": "v" * 9000, "b": "2"},
            {"traceparent": TP, "baggage": "a = 1"},
        ),
        (
            {f"k{i}": "v" for i in range(65)},
            {"traceparent": TP, "baggage": ", ".join(f"k{i} = v" for i in range(64))},
        ),
    ],
    ids=["over-8192-chars", "stops-at-the-long-member", "over-64-members"],
)
def test_opentelemetry_baggage_over_the_limits_is_cut_like_w3c_propagator(
    entries: dict[str, str], sent: dict[str, str]
) -> None:
    # OpenTelemetry's TraceState already refuses more than 32 members.
    with _otel_scope(TP, **entries):
        assert outgoing_headers() == sent


class _Unprintable:
    def __str__(self) -> str:
        raise RuntimeError("no text")


def test_an_unprintable_opentelemetry_value_is_dropped_rather_than_failing_the_call() -> None:
    # set_baggage takes any object, and checks nothing. Only that member is lost.
    with _otel_scope(TP, k=_Unprintable(), userId="bob"):  # type: ignore[arg-type]
        assert outgoing_headers() == {"traceparent": TP, "baggage": "userId = bob"}
        assert _sent(TraceContext(baggage="k=v")) == {"traceparent": TP, "baggage": "k = v"}


def test_a_tracestate_that_writes_no_text_is_left_out_as_opentelemetry_does() -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage
    from opentelemetry import context as otel_context

    class _OddTraceState(trace.TraceState):
        def to_header(self) -> str:
            return 5  # type: ignore[return-value]

    span_context = trace.SpanContext(
        trace_id=int(TP[3:35], 16),
        span_id=int(TP[36:52], 16),
        is_remote=False,
        trace_flags=trace.TraceFlags(1),
        trace_state=_OddTraceState(),
    )
    ctx = baggage.set_baggage("k", "v", trace.set_span_in_context(trace.NonRecordingSpan(span_context)))
    token = otel_context.attach(ctx)
    try:
        # As with OpenTelemetry's own propagator, the traceparent and baggage still go.
        assert outgoing_headers() == {"traceparent": TP, "baggage": "k = v"}
        assert _sent(TraceContext(baggage="userId=alice")) == {"traceparent": TP, "baggage": "userId = alice"}
    finally:
        otel_context.detach(token)


def test_a_span_that_cannot_be_read_is_left_out_and_says_so_once(
    monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
) -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import context as otel_context

    from uipath_ipc import trace_context

    class _Unreadable(trace.NonRecordingSpan):
        def get_span_context(self) -> trace.SpanContext:
            raise RuntimeError("no span context")

    monkeypatch.setattr(trace_context, "_read_failure_logged", False)
    from opentelemetry import baggage

    ctx = baggage.set_baggage("k", "v", trace.set_span_in_context(_Unreadable(trace.INVALID_SPAN_CONTEXT)))
    token = otel_context.attach(ctx)
    try:
        with caplog.at_level("WARNING", logger="uipath_ipc.trace_context"):
            # The baggage still goes, as with OpenTelemetry's own propagators.
            assert outgoing_headers() == {"baggage": "k = v"}
            assert outgoing_headers() == {"baggage": "k = v"}
            # A scope's own baggage still goes.
            assert _sent(TraceContext(baggage="userId=alice")) == {"baggage": "userId = alice"}
    finally:
        otel_context.detach(token)
    assert [r.getMessage() for r in caplog.records] == [
        "Part of the OpenTelemetry context couldn't be read, so it isn't sent."
    ]


def test_incoming_scope_sets_the_remote_parent_and_baggage() -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage

    received = TraceContext(TP, "congo=t61rcWkgMzE", "userId = alice, x = a%20b")
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


def test_incoming_scope_hands_opentelemetry_the_first_of_a_repeated_key() -> None:
    pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage

    with incoming_scope(TraceContext(baggage="k=1,j=2,k=3,=x,z=4"), enabled=True):
        assert list(baggage.get_all().items()) == [("k", "1"), ("j", "2")]
        assert outgoing_headers() == {"baggage": "k = 1, j = 2"}


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
            # No span context, so a nested call sends the baggage alone, without the tracestate.
            assert outgoing_headers() == {"baggage": "k = v"}


@pytest.mark.parametrize(
    ("tracestate", "parsed", "forwarded"),
    [
        ("congo=t61rcWkgMzE, rojo=00f0", "congo=t61rcWkgMzE,rojo=00f0", "congo=t61rcWkgMzE, rojo=00f0"),
        ("rojo=00f0,Congo=t61", "rojo=00f0", "rojo=00f0"),
        ("congo=t61rcWkgMzE,Bad Member", "congo=t61rcWkgMzE", "congo=t61rcWkgMzE"),
        ("congo=t61,congo=x", "", "congo=t61,congo=x"),
        # OpenTelemetry drops a tracestate of more than 32 members.
        (",".join(f"k{i}=v" for i in range(33)), "", ",".join(f"k{i}=v" for i in range(33))),
    ],
    ids=["whitespace", "invalid-key", "bad-member", "duplicate-key", "over-32-members"],
)
def test_received_tracestate_is_forwarded_as_received(
    tracestate: str, parsed: str, forwarded: str
) -> None:
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
        assert outgoing_headers()["tracestate"] == forwarded  # type: ignore[index]
        # A child span inherits the TraceState unchanged.
        child = trace.SpanContext(parent.trace_id, 0x1234, False, parent.trace_flags, parent.trace_state)
        assert outgoing_tracestate_under(child) == forwarded
        changed = trace.SpanContext(
            parent.trace_id, 0x1234, False, parent.trace_flags, trace.TraceState([("other", "1")])
        )
        assert outgoing_tracestate_under(changed) == "other=1"
