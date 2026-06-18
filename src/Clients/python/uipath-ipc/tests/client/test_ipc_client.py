"""Tests for IpcClient + dynamic proxy."""

from __future__ import annotations

import asyncio
import json
import struct
from abc import ABC, abstractmethod

import pytest

from uipath_ipc import (
    INFINITE_REQUEST_TIMEOUT,
    IpcClient,
    Message,
    RemoteException,
)
from uipath_ipc.transport.base import ClientTransport
from uipath_ipc.wire import Error, MessageType, Response


# --- a fake transport that lets us drive both sides -----------------------

class _BufferWriter:
    def __init__(self) -> None:
        self.buffer = bytearray()

    def write(self, data: bytes) -> None:
        self.buffer.extend(data)

    async def drain(self) -> None:
        pass

    def close(self) -> None:
        pass

    async def wait_closed(self) -> None:
        pass


class _FakeTransport(ClientTransport):
    def __init__(self) -> None:
        self.reader = asyncio.StreamReader()
        self.writer = _BufferWriter()

    async def connect(self):  # type: ignore[override]
        return self.reader, self.writer  # type: ignore[return-value]


class _HangingTransport(ClientTransport):
    """Never completes a connect — stands in for a black-holed/unreachable host."""

    async def connect(self):  # type: ignore[override]
        await asyncio.Event().wait()
        raise AssertionError("unreachable")  # pragma: no cover


def _response_frame(resp: Response) -> bytes:
    payload = resp.to_json().encode("utf-8")
    return struct.pack("<Bi", int(MessageType.RESPONSE), len(payload)) + payload


# --- example contract -----------------------------------------------------

class IComputingService(ABC):
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...

    @abstractmethod
    async def Notify(self, message: str) -> None: ...


class ITimed(ABC):
    @abstractmethod
    async def DoWork(self, m: object) -> None: ...


async def _sent_request(writer: _BufferWriter) -> dict:
    """Poll for the REQUEST frame instead of assuming one event-loop turn —
    on 3.10/3.11 asyncio.wait_for schedules the wrapped coroutine a turn
    later than on 3.12+, so a single sleep(0) is not enough."""
    for _ in range(50):
        if len(writer.buffer) >= 5:
            buf = bytes(writer.buffer)
            payload_len = int.from_bytes(buf[1:5], "little", signed=True)
            if len(buf) >= 5 + payload_len:
                return json.loads(buf[5 : 5 + payload_len].decode("utf-8"))
        await asyncio.sleep(0)
    raise AssertionError("no complete REQUEST frame was written")


# --- proxy tests ----------------------------------------------------------

async def test_proxy_round_trips_a_call() -> None:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.5, 2.5))
        await asyncio.sleep(0)
        t.reader.feed_data(_response_frame(Response(request_id="1", data="4.0")))
        result = await asyncio.wait_for(task, timeout=1.0)
        assert result == 4.0


async def test_proxy_serializes_args_as_individual_json_strings() -> None:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.5, 2.5))
        await asyncio.sleep(0)

        # Decode the request that was written
        buf = bytes(t.writer.buffer)
        msg_type = buf[0]
        payload_len = int.from_bytes(buf[1:5], "little", signed=True)
        payload = buf[5:5 + payload_len].decode("utf-8")
        req_obj = json.loads(payload)

        assert msg_type == int(MessageType.REQUEST)
        assert req_obj["Endpoint"] == "IComputingService"
        assert req_obj["MethodName"] == "AddFloats"
        assert req_obj["Parameters"] == ["1.5", "2.5"]   # each arg JSON-encoded

        # Tidy up the pending task
        t.reader.feed_data(_response_frame(Response(request_id="1", data="4.0")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_proxy_void_return() -> None:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.Notify("hi"))
        await asyncio.sleep(0)
        # Response with no data
        t.reader.feed_data(_response_frame(Response(request_id="1", data=None)))
        result = await asyncio.wait_for(task, timeout=1.0)
        assert result is None


async def test_proxy_empty_data_return() -> None:
    """A void op can answer with an empty Data *string* (not null) — e.g. .NET
    CoreIpc for a Task-returning method. json.loads('') would throw, so the
    proxy must treat empty Data as None too."""
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.Notify("hi"))
        await asyncio.sleep(0)
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        result = await asyncio.wait_for(task, timeout=1.0)
        assert result is None


async def test_proxy_raises_on_error_response() -> None:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)
        err = Error(message="boom", type_name="System.InvalidOperationException")
        t.reader.feed_data(_response_frame(Response(request_id="1", error=err)))

        with pytest.raises(RemoteException) as ex_info:
            await asyncio.wait_for(task, timeout=1.0)
        assert ex_info.value.message == "boom"
        assert ex_info.value.type_name == "System.InvalidOperationException"


async def test_proxy_unknown_method_raises_attribute_error() -> None:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        with pytest.raises(AttributeError):
            _ = svc.DoesNotExist  # type: ignore[attr-defined]


# --- per-call timeout (Message argument) -----------------------------------

async def test_message_arg_sets_per_call_timeout() -> None:
    """A Message arg's request_timeout overrides the client-wide default for
    this call and rides the wire (TimeoutInSeconds); a payload-less Message
    serializes to {}."""
    t = _FakeTransport()
    async with IpcClient(t) as client:  # client-wide timeout is None
        svc = client.get_proxy(ITimed)
        task = asyncio.create_task(svc.DoWork(Message(request_timeout=2.0)))
        await asyncio.sleep(0)
        req = await _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == 2.0
        assert req["Parameters"] == ["{}"]
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_message_arg_with_payload_serializes_payload() -> None:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(ITimed)
        task = asyncio.create_task(
            svc.DoWork(Message(payload={"k": 1}, request_timeout=5.0))
        )
        await asyncio.sleep(0)
        req = await _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == 5.0
        assert req["Parameters"] == ['{"Payload": {"k": 1}}']
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_infinite_request_timeout_disables_client_deadline() -> None:
    """A negative request_timeout (INFINITE_REQUEST_TIMEOUT = -0.001, the
    .NET Timeout.InfiniteTimeSpan rendition) rides the wire verbatim and
    applies NO client-side deadline: a response arriving 'late' still wins.
    (With a naive wait_for(-0.001) this would TimeoutError instantly.)"""
    t = _FakeTransport()
    async with IpcClient(t, request_timeout=5.0) as client:  # finite default
        svc = client.get_proxy(ITimed)
        task = asyncio.create_task(
            svc.DoWork(Message(request_timeout=INFINITE_REQUEST_TIMEOUT))
        )
        await asyncio.sleep(0)
        req = await _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == -0.001
        await asyncio.sleep(0.1)  # response arrives later — call must survive
        assert not task.done()
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        assert await asyncio.wait_for(task, timeout=1.0) is None


async def test_message_wire_body_serializes_at_top_level() -> None:
    """wire_body is the .NET Message-SUBCLASS rendition: the dict IS the
    argument's wire form (top-level fields, no Payload wrapper)."""
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(ITimed)
        task = asyncio.create_task(svc.DoWork(
            Message(wire_body={"ServiceUrl": None}, request_timeout=INFINITE_REQUEST_TIMEOUT)
        ))
        await asyncio.sleep(0)
        req = await _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == -0.001
        assert json.loads(req["Parameters"][0]) == {"ServiceUrl": None}
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_message_wire_body_accepts_a_dataclass_directly() -> None:
    """wire_body runs through to_wire, so a timeout-carrying DTO can be handed
    in as a dataclass instance (no explicit .to_wire()) and still serializes to
    top-level fields — the way a per-call timeout is attached to a typed arg."""
    from dataclasses import dataclass

    @dataclass
    class _SignIn:
        ServiceUrl: str | None = None

    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(ITimed)
        task = asyncio.create_task(svc.DoWork(
            Message(wire_body=_SignIn(ServiceUrl="https://x"),
                    request_timeout=INFINITE_REQUEST_TIMEOUT)
        ))
        await asyncio.sleep(0)
        req = await _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == -0.001
        assert json.loads(req["Parameters"][0]) == {"ServiceUrl": "https://x"}
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        await asyncio.wait_for(task, timeout=1.0)


def test_message_payload_and_wire_body_are_mutually_exclusive() -> None:
    with pytest.raises(ValueError):
        Message(payload={"a": 1}, wire_body={"b": 2})


# --- hooks (before_connect / before_call) ----------------------------------

async def test_before_connect_fires_before_connecting() -> None:
    events: list[str] = []
    t = _FakeTransport()

    async def hook() -> None:
        events.append("connect")

    client = IpcClient(t, before_connect=hook)
    assert events == []  # not until first call triggers a connect
    svc = client.get_proxy(IComputingService)
    task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
    await asyncio.sleep(0)
    assert events == ["connect"]
    t.reader.feed_data(_response_frame(Response(request_id="1", data="3.0")))
    await asyncio.wait_for(task, timeout=1.0)
    await client.aclose()


async def test_before_call_fires_with_call_info() -> None:
    seen: list[object] = []
    t = _FakeTransport()

    async def hook(ci: object) -> None:
        seen.append(ci)

    async with IpcClient(t, before_call=hook) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.5, 2.5))
        await asyncio.sleep(0)
        assert len(seen) == 1
        assert seen[0].endpoint == "IComputingService"  # type: ignore[attr-defined]
        assert seen[0].method_name == "AddFloats"  # type: ignore[attr-defined]
        assert seen[0].arguments == (1.5, 2.5)  # type: ignore[attr-defined]
        t.reader.feed_data(_response_frame(Response(request_id="1", data="4.0")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_before_call_raising_aborts_the_call() -> None:
    t = _FakeTransport()

    async def hook(ci: object) -> None:
        raise RuntimeError("blocked")

    async with IpcClient(t, before_call=hook) as client:
        svc = client.get_proxy(IComputingService)
        with pytest.raises(RuntimeError, match="blocked"):
            await asyncio.wait_for(svc.AddFloats(1.0, 2.0), timeout=1.0)
        assert len(t.writer.buffer) == 0  # nothing was sent


# --- client lifecycle tests -----------------------------------------------

async def test_client_lazily_connects() -> None:
    """No connection is opened until the first call."""
    t = _FakeTransport()
    client = IpcClient(t)
    assert client._connection is None
    # Trigger a call
    svc = client.get_proxy(IComputingService)
    task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
    await asyncio.sleep(0)
    assert client._connection is not None
    # Tidy up
    t.reader.feed_data(_response_frame(Response(request_id="1", data="3.0")))
    await asyncio.wait_for(task, timeout=1.0)
    await client.aclose()


async def test_client_async_context_closes_connection() -> None:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)
        t.reader.feed_data(_response_frame(Response(request_id="1", data="3.0")))
        await asyncio.wait_for(task, timeout=1.0)
        assert client._connection is not None
    # After exit, connection should be cleared
    assert client._connection is None


async def test_connect_is_bounded_by_request_timeout() -> None:
    """The dial runs INSIDE the call deadline: a transport that never connects
    raises asyncio.TimeoutError at ~request_timeout instead of hanging on the
    (long) OS connect timeout."""
    client = IpcClient(_HangingTransport(), request_timeout=0.3)
    try:
        svc = client.get_proxy(IComputingService)
        start = asyncio.get_running_loop().time()
        with pytest.raises(asyncio.TimeoutError):
            await svc.AddFloats(1.0, 2.0)
        assert asyncio.get_running_loop().time() - start < 2.0
    finally:
        await client.aclose()


async def test_ensure_connected_raises_after_close() -> None:
    """aclose() sets a closed flag under the connect lock, so a later (or
    racing) connect can't silently revive the client."""
    client = IpcClient(_FakeTransport())
    await client.aclose()
    with pytest.raises(ConnectionError):
        await client._ensure_connected()


async def test_before_connect_calling_same_client_raises_not_deadlocks() -> None:
    """A before_connect hook runs inside the connect lock; if it calls back into
    the same client it must raise a clear error rather than deadlock silently."""
    client = IpcClient(_FakeTransport())

    async def hook() -> None:
        # Re-entrant call on the same client (would deadlock without the guard).
        await client.get_proxy(IComputingService).AddFloats(1.0, 2.0)

    client._before_connect = hook
    try:
        with pytest.raises(RuntimeError, match="before_connect"):
            await asyncio.wait_for(
                client.get_proxy(IComputingService).AddFloats(3.0, 4.0), timeout=2
            )
    finally:
        await client.aclose()


async def test_del_warns_and_abandons_open_connection() -> None:
    """A client GC'd without aclose() warns and best-effort closes its
    connection (so the receive-loop task doesn't leak)."""

    class _FakeConn:
        def __init__(self) -> None:
            self.is_closed = False
            self.abandoned = False

        def _abandon(self) -> None:
            self.abandoned = True
            self.is_closed = True

    client = IpcClient(_FakeTransport())
    client._connection = _FakeConn()  # type: ignore[assignment]
    with pytest.warns(ResourceWarning):
        client.__del__()
    assert client._connection.abandoned  # type: ignore[attr-defined]


async def test_before_call_reports_new_connection_only_on_first_call() -> None:
    """CallInfo.new_connection is True for the call that opens the connection,
    False for subsequent calls reusing it (matches .NET CallInfo.NewConnection)."""
    seen: list[bool] = []
    t = _FakeTransport()
    async with IpcClient(t, before_call=lambda ci: seen.append(ci.new_connection)) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))  # opens the connection
        await asyncio.sleep(0)
        t.reader.feed_data(_response_frame(Response(request_id="1", data="3.0")))
        await asyncio.wait_for(task, timeout=1.0)
        task2 = asyncio.create_task(svc.AddFloats(3.0, 4.0))  # reuses it
        await asyncio.sleep(0)
        t.reader.feed_data(_response_frame(Response(request_id="2", data="7.0")))
        await asyncio.wait_for(task2, timeout=1.0)
    assert seen == [True, False]


async def test_message_request_timeout_zero_is_no_override() -> None:
    """Message(request_timeout=0) is the 'no override' sentinel (.NET's `when
    requestTimeout != TimeSpan.Zero`): it leaves the wire TimeoutInSeconds at 0
    (use the server's default). The client-wide request_timeout is a LOCAL
    deadline and never rides the wire, so it does NOT surface here either."""
    t = _FakeTransport()
    async with IpcClient(t, request_timeout=2.0) as client:
        svc = client.get_proxy(ITimed)
        task = asyncio.create_task(svc.DoWork(Message(request_timeout=0)))
        await asyncio.sleep(0)
        req = await _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == 0  # no per-call override; 2.0 stays local
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_negative_per_call_timeout_is_clamped_to_infinite_sentinel() -> None:
    """A negative per-call Message timeout other than -0.001 is normalized to
    the infinite sentinel (-0.001) on the wire: .NET treats only -1ms as
    Timeout.InfiniteTimeSpan and rejects other negatives."""
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(ITimed)
        task = asyncio.create_task(svc.DoWork(Message(request_timeout=-5.0)))
        await asyncio.sleep(0)
        req = await _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == -0.001  # clamped, not -5.0
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_duplicate_callback_endpoint_name_raises() -> None:
    """Two callback contracts whose ``__name__`` collides would map to the same
    wire endpoint (one silently shadowing the other). Reject at construction.
    (async so _FakeTransport's StreamReader has a running loop.)"""
    c1 = type("IClash", (), {})
    c2 = type("IClash", (), {})
    with pytest.raises(ValueError, match="duplicate callback endpoint name"):
        IpcClient(_FakeTransport(), callbacks={c1: object(), c2: object()})
