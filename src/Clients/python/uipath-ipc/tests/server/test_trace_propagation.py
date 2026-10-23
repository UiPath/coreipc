"""Trace-context propagation between a Python client and a Python server,
in both directions (forward calls and callbacks), over TCP loopback."""

from __future__ import annotations

import asyncio
import json
from abc import ABC, abstractmethod
from contextlib import asynccontextmanager
from typing import AsyncIterator

import pytest

from uipath_ipc import (
    IpcClient,
    IpcServer,
    Message,
    RemoteException,
    TcpClientTransport,
    TcpServerTransport,
    TelemetryPropagationPolicy,
    TraceContext,
    incoming_trace_context,
    use_trace_context,
)
from uipath_ipc.client.connection import IpcConnection
from uipath_ipc.wire import Request

NONE = TelemetryPropagationPolicy.NONE
INCOMING = TelemetryPropagationPolicy.INCOMING
OUTGOING = TelemetryPropagationPolicy.OUTGOING
BOTH = TelemetryPropagationPolicy.BOTH
TP = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
TP2 = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00"
FULL = TraceContext(TP, "congo=t61rcWkgMzE", "userId=alice,x=a%20b")


def _seen() -> str | None:
    tc = incoming_trace_context()
    return None if tc is None else f"{tc.traceparent}|{tc.tracestate}|{tc.baggage}"


class ITraceProbe(ABC):
    @abstractmethod
    async def Probe(self) -> str | None: ...

    @abstractmethod
    async def ProbeViaCallback(self, m: Message) -> str | None: ...

    @abstractmethod
    async def ProbeViaCallbackWithTraceContext(
        self, traceparent: str, baggage: str | None, m: Message
    ) -> str | None: ...

    @abstractmethod
    async def Fire(self) -> None: ...


class ITraceProbeCallback(ABC):
    @abstractmethod
    async def Probe(self) -> str | None: ...


class TraceProbe:
    def __init__(self) -> None:
        self.fired: list[str | None] = []

    async def Probe(self) -> str | None:
        return _seen()

    async def ProbeViaCallback(self, m: Message) -> str | None:
        assert m.client is not None
        return await m.client.get_callback(ITraceProbeCallback).Probe()

    async def ProbeViaCallbackWithTraceContext(
        self, traceparent: str, baggage: str | None, m: Message
    ) -> str | None:
        with use_trace_context(TraceContext(traceparent, baggage=baggage)):
            return await self.ProbeViaCallback(m)

    async def Fire(self) -> None:
        self.fired.append(_seen())


class TraceProbeCallback:
    async def Probe(self) -> str | None:
        return _seen()


class NestedCallCallback:
    def __init__(self) -> None:
        self.client: IpcClient | None = None

    async def Probe(self) -> str | None:
        assert self.client is not None
        return await self.client.get_proxy(ITraceProbe).Probe()


def _attach_span() -> object:
    from opentelemetry import context as otel_context
    from opentelemetry import trace

    return otel_context.attach(trace.set_span_in_context(trace.NonRecordingSpan(trace.SpanContext(
        trace_id=int(TP[3:35], 16), span_id=int(TP[36:52], 16), is_remote=False,
        trace_flags=trace.TraceFlags(1),
    ))))


@asynccontextmanager
async def _pair(
    *,
    client_policy: TelemetryPropagationPolicy = BOTH,
    server_policy: TelemetryPropagationPolicy = BOTH,
    probe: TraceProbe | None = None,
    callback: object | None = None,
) -> AsyncIterator[tuple[IpcClient, TraceProbe]]:
    probe = probe or TraceProbe()
    server = IpcServer(
        TcpServerTransport("127.0.0.1", 0),
        {ITraceProbe: probe},
        telemetry_propagation=server_policy,
    )
    async with server:
        assert server.handle is not None
        host, port = server.handle.sockets[0].getsockname()[:2]  # type: ignore[attr-defined]
        client = IpcClient(
            TcpClientTransport(host, port),
            request_timeout=5,
            callbacks={ITraceProbeCallback: callback or TraceProbeCallback()},
            telemetry_propagation=client_policy,
        )
        async with client:
            yield client, probe


@pytest.fixture
def sent_requests(monkeypatch: pytest.MonkeyPatch) -> list[Request]:
    sent: list[Request] = []
    original = IpcConnection.send_request

    async def capture(self: IpcConnection, req: Request, **kwargs: object):  # type: ignore[no-untyped-def]
        sent.append(req)
        return await original(self, req, **kwargs)  # type: ignore[arg-type]

    monkeypatch.setattr(IpcConnection, "send_request", capture)
    return sent


def _sent_headers(sent: list[Request], endpoint: str) -> list[object]:
    return [req.to_dict().get("Headers") for req in sent if req.endpoint == endpoint]


async def test_override_reaches_the_server(sent_requests: list[Request]) -> None:
    async with _pair() as (client, _):
        with use_trace_context(FULL):
            seen = await client.get_proxy(ITraceProbe).Probe()
    assert seen == f"{TP}|congo=t61rcWkgMzE|userId=alice,x=a%20b"
    assert sent_requests[0].to_dict()["Headers"] == {
        "traceparent": TP, "tracestate": "congo=t61rcWkgMzE", "baggage": "userId=alice,x=a%20b",
    }


async def test_no_ambient_context_sends_nothing(sent_requests: list[Request]) -> None:
    async with _pair() as (client, _):
        assert await client.get_proxy(ITraceProbe).Probe() is None
    assert "Headers" not in sent_requests[0].to_dict()


async def test_client_policy_none_sends_nothing(sent_requests: list[Request]) -> None:
    async with _pair(client_policy=NONE) as (client, _):
        with use_trace_context(FULL):
            assert await client.get_proxy(ITraceProbe).Probe() is None
    assert "Headers" not in sent_requests[0].to_dict()


async def test_server_policy_none_ignores_headers() -> None:
    async with _pair(server_policy=NONE) as (client, _):
        with use_trace_context(FULL):
            assert await client.get_proxy(ITraceProbe).Probe() is None


async def test_one_way_handler_sees_the_trace_context() -> None:
    async with _pair() as (client, probe):
        with use_trace_context(TraceContext(TP)):
            await client.get_proxy(ITraceProbe).Fire()
        for _ in range(500):
            if probe.fired:
                break
            await asyncio.sleep(0.01)
    assert probe.fired == [f"{TP}|None|None"]


async def test_callback_honors_an_override_set_by_the_server() -> None:
    async with _pair() as (client, _):
        with use_trace_context(FULL):
            seen = await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(TP2, None)
    assert seen == f"{TP2}|None|None"


async def test_callback_propagates_the_opentelemetry_context() -> None:
    pytest.importorskip("opentelemetry.trace")  # without it nested calls have no ambient context
    async with _pair() as (client, _):
        with use_trace_context(FULL):
            seen = await client.get_proxy(ITraceProbe).ProbeViaCallback()
    # Propagated through the OpenTelemetry context (remote parent), so the
    # callback carries the same traceparent and decoded/re-encoded baggage.
    assert seen == f"{TP}|congo=t61rcWkgMzE|userId=alice,x=a%20b"


async def test_callback_forwards_a_tracestate_opentelemetry_rejects() -> None:
    pytest.importorskip("opentelemetry.trace")
    async with _pair() as (client, _):
        with use_trace_context(TraceContext(TP, "Congo=t61,rojo=00f0")):
            seen = await client.get_proxy(ITraceProbe).ProbeViaCallback()
    assert seen == f"{TP}|Congo=t61,rojo=00f0|None"


async def test_received_values_are_not_the_callback_override(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    import uipath_ipc.trace_context as tc_module

    monkeypatch.setattr(tc_module, "_from_opentelemetry", lambda: None)
    async with _pair() as (client, _):
        with use_trace_context(FULL):
            assert await client.get_proxy(ITraceProbe).ProbeViaCallback() is None


async def test_client_policy_none_ignores_callback_headers() -> None:
    async with _pair(client_policy=NONE) as (client, _):
        assert await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(TP2, None) is None


async def test_server_policy_none_sends_no_callback_headers() -> None:
    async with _pair(server_policy=NONE) as (client, _):
        assert await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(TP2, None) is None


async def test_client_outgoing_only_sends_but_ignores_callback_headers(
    sent_requests: list[Request],
) -> None:
    async with _pair(client_policy=OUTGOING) as (client, _):
        with use_trace_context(FULL):
            seen = await client.get_proxy(ITraceProbe).Probe()
        callback_seen = await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(TP2, None)
    assert seen == f"{TP}|congo=t61rcWkgMzE|userId=alice,x=a%20b"
    assert callback_seen is None
    assert _sent_headers(sent_requests, "ITraceProbeCallback") == [{"traceparent": TP2}]


async def test_client_incoming_only_reads_callback_headers_but_sends_nothing(
    sent_requests: list[Request],
) -> None:
    async with _pair(client_policy=INCOMING) as (client, _):
        with use_trace_context(FULL):
            seen = await client.get_proxy(ITraceProbe).Probe()
            callback_seen = await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(TP2, None)
    assert seen is None
    assert callback_seen == f"{TP2}|None|None"
    assert _sent_headers(sent_requests, "ITraceProbe") == [None, None]


async def test_server_incoming_only_reads_headers_but_sends_no_callback_headers(
    sent_requests: list[Request],
) -> None:
    async with _pair(server_policy=INCOMING) as (client, _):
        with use_trace_context(FULL):
            seen = await client.get_proxy(ITraceProbe).Probe()
        callback_seen = await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(TP2, None)
    assert seen == f"{TP}|congo=t61rcWkgMzE|userId=alice,x=a%20b"
    assert callback_seen is None
    assert _sent_headers(sent_requests, "ITraceProbeCallback") == [None]


async def test_server_outgoing_only_sends_callback_headers_but_ignores_incoming(
    sent_requests: list[Request],
) -> None:
    async with _pair(server_policy=OUTGOING) as (client, _):
        with use_trace_context(FULL):
            seen = await client.get_proxy(ITraceProbe).Probe()
            callback_seen = await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(TP2, None)
    assert seen is None
    assert callback_seen == f"{TP2}|None|None"
    assert _sent_headers(sent_requests, "ITraceProbe")[0] == {
        "traceparent": TP, "tracestate": "congo=t61rcWkgMzE", "baggage": "userId=alice,x=a%20b",
    }


async def test_client_outgoing_only_callback_does_not_send_the_connection_openers_span(
    sent_requests: list[Request],
) -> None:
    pytest.importorskip("opentelemetry.trace")
    from opentelemetry import context as otel_context

    callback = NestedCallCallback()
    async with _pair(client_policy=OUTGOING, callback=callback) as (client, _):
        callback.client = client
        token = _attach_span()
        await client.get_proxy(ITraceProbe).Probe()
        otel_context.detach(token)
        await client.get_proxy(ITraceProbe).ProbeViaCallback()
    assert _sent_headers(sent_requests, "ITraceProbe") == [{"traceparent": TP}, None, None]


async def test_server_outgoing_only_callback_does_not_send_the_server_starters_span(
    sent_requests: list[Request],
) -> None:
    pytest.importorskip("opentelemetry.trace")
    from opentelemetry import context as otel_context

    token = _attach_span()
    async with _pair(server_policy=OUTGOING) as (client, _):
        otel_context.detach(token)
        await client.get_proxy(ITraceProbe).ProbeViaCallback()
    assert _sent_headers(sent_requests, "ITraceProbeCallback") == [None]


async def test_invalid_override_in_the_server_fails_the_call() -> None:
    async with _pair() as (client, _):
        with pytest.raises(RemoteException) as ei:
            await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(
                TP2, ",".join(f"k{i}=v" for i in range(65))
            )
    assert ei.value.type_name == "System.ArgumentException"


async def _send_raw(client: IpcClient, method: str, headers: dict[str, str]) -> RemoteException | str | None:
    conn, _ = await client._ensure_connected()
    req = Request(
        endpoint="ITraceProbe", method_name=method, parameters=[],
        id=conn.next_id(), headers=headers,
    )
    resp = await asyncio.wait_for(conn.send_request(req), timeout=5)
    return RemoteException.from_error(resp.error) if resp.error else resp.data


@pytest.mark.parametrize(
    "headers",
    [
        {"traceparent": TP, "tracestate": ",".join(f"k{i}=v" for i in range(33))},
        {"traceparent": TP, "baggage": ",".join(f"k{i}=v" for i in range(65))},
        {"traceparent": TP, "baggage": "k=" + "v" * 8191},
    ],
    ids=["over-32-tracestate-members", "over-64-baggage-members", "over-8192-baggage-bytes"],
)
async def test_incoming_headers_over_the_limits_reach_the_handler_as_they_are(
    headers: dict[str, str],
) -> None:
    async with _pair() as (client, _):
        data = await _send_raw(client, "Probe", headers)
    assert isinstance(data, str)
    assert json.loads(data) == f"{TP}|{headers.get('tracestate')}|{headers.get('baggage')}"


async def test_over_limit_opentelemetry_baggage_is_received_as_is() -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage
    from opentelemetry import context as otel_context

    ctx = trace.set_span_in_context(trace.NonRecordingSpan(trace.SpanContext(
        trace_id=int(TP[3:35], 16), span_id=int(TP[36:52], 16), is_remote=False,
        trace_flags=trace.TraceFlags(1),
    )))
    for i in range(65):
        ctx = baggage.set_baggage(f"k{i}", "v", ctx)
    async with _pair() as (client, _):
        token = otel_context.attach(ctx)
        try:
            seen = await client.get_proxy(ITraceProbe).Probe()
        finally:
            otel_context.detach(token)
    assert seen == f"{TP}|None|" + ",".join(f"k{i}=v" for i in range(65))


async def test_lone_surrogate_baggage_gets_a_response() -> None:
    async with _pair() as (client, _):
        with use_trace_context(TraceContext(TP, baggage="k=\ud800")):
            seen = await client.get_proxy(ITraceProbe).Probe()
    assert seen == f"{TP}|None|k=\ud800"


async def test_a_traceparent_opentelemetry_rejects_gives_the_handler_no_remote_parent() -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage

    class Probe(TraceProbe):
        async def Probe(self) -> str | None:
            span_context = trace.get_current_span().get_span_context()
            return f"{_seen()}|{span_context.is_valid}|{dict(baggage.get_all())}"

    async with _pair(probe=Probe()) as (client, _):
        data = await _send_raw(
            client, "Probe", {"traceparent": "garbage", "tracestate": "congo=t61", "baggage": "k=v"}
        )
    assert isinstance(data, str)
    assert json.loads(data) == "garbage|congo=t61|k=v|False|{'k': 'v'}"


async def test_a_tracestate_without_a_remote_parent_is_not_forwarded(
    sent_requests: list[Request],
) -> None:
    pytest.importorskip("opentelemetry.trace")
    async with _pair() as (client, _):
        data = await _send_raw(
            client, "ProbeViaCallback", {"traceparent": "garbage", "tracestate": "Congo=t61"}
        )
    assert data is None
    assert _sent_headers(sent_requests, "ITraceProbeCallback") == [None]


async def test_handler_runs_under_the_remote_opentelemetry_parent() -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage

    seen: dict[str, object] = {}

    class Probe(TraceProbe):
        async def Probe(self) -> str | None:
            sc = trace.get_current_span().get_span_context()
            seen.update(
                trace_id=f"{sc.trace_id:032x}", span_id=f"{sc.span_id:016x}",
                remote=sc.is_remote, baggage=dict(baggage.get_all()),
            )
            return None

    async with _pair(probe=Probe()) as (client, _):
        with use_trace_context(FULL):
            await client.get_proxy(ITraceProbe).Probe()
    assert seen == {
        "trace_id": TP[3:35], "span_id": TP[36:52], "remote": True,
        "baggage": {"userId": "alice", "x": "a b"},
    }
