"""Tests for IpcClient + dynamic proxy."""

from __future__ import annotations

import asyncio
import json
import struct
from abc import ABC, abstractmethod

import pytest

from uipath_ipc import IpcClient, Message, RemoteException
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


def _sent_request(writer: _BufferWriter) -> dict:
    buf = bytes(writer.buffer)
    payload_len = int.from_bytes(buf[1:5], "little", signed=True)
    return json.loads(buf[5 : 5 + payload_len].decode("utf-8"))


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
        req = _sent_request(t.writer)
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
        req = _sent_request(t.writer)
        assert req["TimeoutInSeconds"] == 5.0
        assert req["Parameters"] == ['{"Payload": {"k": 1}}']
        t.reader.feed_data(_response_frame(Response(request_id="1", data="")))
        await asyncio.wait_for(task, timeout=1.0)


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
