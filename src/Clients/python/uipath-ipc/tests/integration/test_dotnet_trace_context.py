"""Trace-context propagation against the .NET PythonClientTestServer, whose
IpcServer has TelemetryPropagation Both and hosts ITraceProbe."""

from __future__ import annotations

import asyncio
import json
from abc import ABC, abstractmethod

import pytest

from uipath_ipc import (
    IpcClient,
    NamedPipeClientTransport,
    RemoteException,
    TelemetryPropagationPolicy,
    TraceContext,
    incoming_trace_context,
    ipc_cancellable,
    use_trace_context,
)
from uipath_ipc.wire import Request

from .conftest import DOTNET_PIPE_NAME

pytestmark = pytest.mark.integration

TRACE_ID = "4bf92f3577b34da6a3ce929d0e0e4736"
SPAN_ID = "00f067aa0ba902b7"
TP = f"00-{TRACE_ID}-{SPAN_ID}-01"
TP2 = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00"
FULL = TraceContext(TP, "congo=t61rcWkgMzE", "userId=alice,x=a%20b")


class ITraceProbe(ABC):
    @ipc_cancellable
    @abstractmethod
    async def Probe(self) -> dict: ...

    @ipc_cancellable
    @abstractmethod
    async def ProbeViaCallback(self) -> dict: ...

    @ipc_cancellable
    @abstractmethod
    async def ProbeViaCallbackWithTraceContext(
        self, traceParent: str, traceState: str | None, baggage: str | None
    ) -> dict: ...


class ITraceProbeCallback(ABC):
    @abstractmethod
    async def Probe(self) -> str: ...


class TraceProbeCallback:
    async def Probe(self) -> str:
        tc = incoming_trace_context()
        return "none" if tc is None else f"{tc.traceparent}|{tc.tracestate}|{tc.baggage}"


def _client(
    policy: TelemetryPropagationPolicy = TelemetryPropagationPolicy.BOTH,
    callback: object | None = None,
) -> IpcClient:
    return IpcClient(
        NamedPipeClientTransport(pipe_name=DOTNET_PIPE_NAME),
        request_timeout=10,
        callbacks={ITraceProbeCallback: callback or TraceProbeCallback()},
        telemetry_propagation=policy,
    )


async def test_dotnet_server_sees_the_override(dotnet_server) -> None:
    async with _client() as client:
        with use_trace_context(FULL):
            result = await client.get_proxy(ITraceProbe).Probe()
    assert result["TraceParent"] == TP
    assert result["TraceState"] == "congo=t61rcWkgMzE"
    assert result["Baggage"] == "userId=alice,x=a%20b"
    assert result["ActivityTraceId"] == TRACE_ID
    assert result["ActivityParentSpanId"] == SPAN_ID
    assert result["ActivitySpanId"] not in (None, SPAN_ID)
    assert result["ActivityBaggage"] == ["userId=alice", "x=a b"]


@pytest.mark.parametrize(
    "policy", [TelemetryPropagationPolicy.NONE, TelemetryPropagationPolicy.INCOMING]
)
async def test_client_without_outgoing_sends_nothing_to_dotnet(
    dotnet_server, policy: TelemetryPropagationPolicy
) -> None:
    async with _client(policy) as client:
        with use_trace_context(FULL):
            result = await client.get_proxy(ITraceProbe).Probe()
    assert result["TraceParent"] is None
    assert result["ActivityTraceId"] is None


async def test_dotnet_server_sees_the_opentelemetry_span(dotnet_server) -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage
    from opentelemetry import context as otel_context

    span_context = trace.SpanContext(
        trace_id=int(TRACE_ID, 16), span_id=int(SPAN_ID, 16), is_remote=False,
        trace_flags=trace.TraceFlags(1),
    )
    ctx = baggage.set_baggage(
        "userId", "alice", trace.set_span_in_context(trace.NonRecordingSpan(span_context))
    )
    token = otel_context.attach(ctx)
    try:
        async with _client() as client:
            result = await client.get_proxy(ITraceProbe).Probe()
    finally:
        otel_context.detach(token)
    assert result["TraceParent"] == TP
    assert result["ActivityTraceId"] == TRACE_ID
    assert result["ActivityParentSpanId"] == SPAN_ID
    assert result["ActivityBaggage"] == ["userId=alice"]


async def test_dotnet_callback_arrives_under_the_servers_client_span(dotnet_server) -> None:
    async with _client() as client:
        with use_trace_context(FULL):
            result = await client.get_proxy(ITraceProbe).ProbeViaCallback()
    assert result["ServerActivityTraceId"] == TRACE_ID
    server_span = result["ServerActivitySpanId"]
    assert server_span not in (None, SPAN_ID)
    traceparent, tracestate, received_baggage = result["Callback"].split("|")
    assert traceparent.startswith(f"00-{TRACE_ID}-") and traceparent.endswith("-01")
    assert traceparent[36:52] not in (server_span, SPAN_ID)
    assert (tracestate, received_baggage) == ("congo=t61rcWkgMzE", "userId=alice,x=a%20b")


async def test_dotnet_callback_opentelemetry_parent_is_the_servers_client_span(dotnet_server) -> None:
    trace = pytest.importorskip("opentelemetry.trace")
    from opentelemetry import baggage

    class Callback:
        async def Probe(self) -> str:
            sc = trace.get_current_span().get_span_context()
            return f"{sc.trace_id:032x}|{sc.span_id:016x}|{sc.is_remote}|{dict(baggage.get_all())}"

    async with _client(callback=Callback()) as client:
        with use_trace_context(FULL):
            result = await client.get_proxy(ITraceProbe).ProbeViaCallback()
    trace_id, parent_span, is_remote, received_baggage = result["Callback"].split("|")
    assert (trace_id, is_remote, received_baggage) == (TRACE_ID, "True", "{'userId': 'alice', 'x': 'a b'}")
    assert parent_span not in (result["ServerActivitySpanId"], SPAN_ID)


async def test_dotnet_callback_carries_the_servers_override(dotnet_server) -> None:
    async with _client() as client:
        with use_trace_context(FULL):
            result = await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(
                TP2, "a=1", "k=v"
            )
    assert result["Callback"] == f"{TP2}|a=1|k=v"


@pytest.mark.parametrize(
    "policy", [TelemetryPropagationPolicy.NONE, TelemetryPropagationPolicy.OUTGOING]
)
async def test_client_without_incoming_ignores_dotnet_callback_headers(
    dotnet_server, policy: TelemetryPropagationPolicy
) -> None:
    async with _client(policy) as client:
        result = await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(
            TP2, None, None
        )
    assert result["Callback"] == "none"


async def test_invalid_server_override_fails_the_dotnet_call(dotnet_server) -> None:
    async with _client() as client:
        with pytest.raises(RemoteException) as ei:
            await client.get_proxy(ITraceProbe).ProbeViaCallbackWithTraceContext(
                TP2, None, ",".join(f"k{i}=v" for i in range(65))
            )
    assert ei.value.type_name == "System.ArgumentException"


async def test_dotnet_server_takes_headers_over_the_limits_as_they_are(dotnet_server) -> None:
    baggage = ",".join(f"k{i}=v" for i in range(65))
    async with _client() as client:
        conn, _ = await client._ensure_connected()
        req = Request(
            endpoint="ITraceProbe", method_name="Probe", parameters=[""],
            id=conn.next_id(), headers={"traceparent": TP, "baggage": baggage},
        )
        resp = await asyncio.wait_for(conn.send_request(req), timeout=10)
    assert resp.error is None
    assert resp.data is not None
    assert json.loads(resp.data)["Baggage"] == baggage
