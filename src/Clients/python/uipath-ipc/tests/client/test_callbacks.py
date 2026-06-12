"""Unit tests for incoming-request dispatch (callbacks)."""

from __future__ import annotations

import asyncio
import json
import struct
from abc import ABC, abstractmethod

import pytest

from uipath_ipc.client import IpcConnection
from uipath_ipc.wire import (
    CancellationRequest,
    MessageType,
    Request,
    Response,
)


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


def _request_frame(req: Request) -> bytes:
    payload = req.to_json().encode("utf-8")
    return struct.pack("<Bi", int(MessageType.REQUEST), len(payload)) + payload


def _cancellation_frame(request_id: str) -> bytes:
    payload = (
        CancellationRequest(request_id=request_id).to_json().encode("utf-8")
    )
    return (
        struct.pack("<Bi", int(MessageType.CANCELLATION_REQUEST), len(payload))
        + payload
    )


def _split_frames(buf: bytes) -> list[tuple[int, bytes]]:
    out = []
    i = 0
    while i + 5 <= len(buf):
        msg_type = buf[i]
        length = int.from_bytes(buf[i + 1 : i + 5], "little", signed=True)
        i += 5
        out.append((msg_type, bytes(buf[i : i + length])))
        i += length
    return out


async def _wait_for_frames(writer: _BufferWriter, count: int, timeout: float = 1.0) -> list[tuple[int, bytes]]:
    """Poll the buffer until `count` frames are present or `timeout` elapses."""
    deadline = asyncio.get_running_loop().time() + timeout
    while True:
        frames = _split_frames(bytes(writer.buffer))
        if len(frames) >= count:
            return frames
        if asyncio.get_running_loop().time() > deadline:
            pytest.fail(f"only saw {len(frames)} frames after {timeout}s; expected {count}")
        await asyncio.sleep(0.01)


# --- a sample callback contract and impl ---------------------------------

class IClientCallback(ABC):
    @abstractmethod
    async def EchoToClient(self, value: str) -> str: ...

    @abstractmethod
    async def AddOnClient(self, x: int, y: int) -> int: ...

    @abstractmethod
    async def RaiseOnClient(self) -> bool: ...

    @abstractmethod
    async def WaitOnClient(self, seconds: float) -> bool: ...


class _DummyCallback(IClientCallback):
    def __init__(self) -> None:
        self.echo_calls: list[str] = []

    async def EchoToClient(self, value: str) -> str:
        self.echo_calls.append(value)
        return f"echoed: {value}"

    async def AddOnClient(self, x: int, y: int) -> int:
        return x + y

    async def RaiseOnClient(self) -> bool:
        raise ValueError("boom from client callback")

    async def WaitOnClient(self, seconds: float) -> bool:
        await asyncio.sleep(seconds)
        return True


def _make_connection(
    callback: _DummyCallback | None = None,
) -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter]:
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    callbacks = {"IClientCallback": callback} if callback else None
    conn = IpcConnection(reader, writer, callbacks=callbacks)  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer


# --- happy path ----------------------------------------------------------

async def test_incoming_request_dispatched_to_callback_method() -> None:
    cb = _DummyCallback()
    conn, reader, writer = _make_connection(cb)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IClientCallback",
            method_name="EchoToClient",
            parameters=['"hi"'],
            id="42",
        )))
        frames = await _wait_for_frames(writer, count=1)

        assert frames[0][0] == int(MessageType.RESPONSE)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert resp.request_id == "42"
        assert json.loads(resp.data) == "echoed: hi"
        assert resp.error is None
        assert cb.echo_calls == ["hi"]
    finally:
        await conn.aclose()


async def test_callback_with_multiple_args() -> None:
    cb = _DummyCallback()
    conn, reader, writer = _make_connection(cb)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IClientCallback",
            method_name="AddOnClient",
            parameters=["3", "4"],
            id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == 7
    finally:
        await conn.aclose()


async def test_concurrent_incoming_requests() -> None:
    cb = _DummyCallback()
    conn, reader, writer = _make_connection(cb)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IClientCallback",
            method_name="EchoToClient",
            parameters=['"a"'],
            id="1",
        )))
        reader.feed_data(_request_frame(Request(
            endpoint="IClientCallback",
            method_name="EchoToClient",
            parameters=['"b"'],
            id="2",
        )))
        frames = await _wait_for_frames(writer, count=2)

        ids = sorted(
            Response.from_json(f[1].decode("utf-8")).request_id for f in frames
        )
        assert ids == ["1", "2"]
        assert sorted(cb.echo_calls) == ["a", "b"]
    finally:
        await conn.aclose()


# --- error paths ---------------------------------------------------------

async def test_callback_exception_returns_error_response() -> None:
    cb = _DummyCallback()
    conn, reader, writer = _make_connection(cb)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IClientCallback",
            method_name="RaiseOnClient",
            parameters=[],
            id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))

        assert resp.error is not None
        assert resp.error.message == "boom from client callback"
        assert resp.error.type_name == "ValueError"
        assert resp.error.stack_trace is not None
        assert resp.data is None
    finally:
        await conn.aclose()


async def test_unknown_endpoint_returns_error() -> None:
    conn, reader, writer = _make_connection(None)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="INonExistent",
            method_name="Foo",
            parameters=[],
            id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))

        assert resp.error is not None
        assert "INonExistent" in resp.error.message
        # .NET wire type name, so a .NET caller can match with
        # RemoteException.Is<EndpointNotFoundException>().
        assert resp.error.type_name == "UiPath.Ipc.EndpointNotFoundException"
    finally:
        await conn.aclose()


async def test_unknown_method_returns_error() -> None:
    cb = _DummyCallback()
    conn, reader, writer = _make_connection(cb)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IClientCallback",
            method_name="DoesNotExist",
            parameters=[],
            id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))

        assert resp.error is not None
        assert "DoesNotExist" in resp.error.message
        assert resp.error.type_name == "UiPath.Ipc.MethodNotFoundException"
    finally:
        await conn.aclose()


# --- server cancellation -------------------------------------------------

async def test_server_cancellation_aborts_in_flight_callback() -> None:
    cb = _DummyCallback()
    conn, reader, writer = _make_connection(cb)
    try:
        # Slow callback: 5 seconds
        reader.feed_data(_request_frame(Request(
            endpoint="IClientCallback",
            method_name="WaitOnClient",
            parameters=["5"],
            id="42",
        )))
        await asyncio.sleep(0.05)  # let it start

        # Server cancels mid-flight
        reader.feed_data(_cancellation_frame("42"))

        frames = await _wait_for_frames(writer, count=1, timeout=1.0)
        resp = Response.from_json(frames[0][1].decode("utf-8"))

        assert resp.error is not None
        assert resp.error.type_name == "System.OperationCanceledException"
    finally:
        await conn.aclose()
