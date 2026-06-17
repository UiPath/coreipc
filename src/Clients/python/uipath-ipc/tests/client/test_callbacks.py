"""Unit tests for incoming-request dispatch (callbacks)."""

from __future__ import annotations

import asyncio
import json
import logging
import struct
from abc import ABC, abstractmethod

import pytest

from uipath_ipc.client import IpcConnection
from uipath_ipc.errors import RemoteException
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
    callbacks = (
        {"IClientCallback": (IClientCallback, callback)} if callback else None
    )
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

# --- contract-membership dispatch guard (security) ------------------------

class IGuarded(ABC):
    @abstractmethod
    async def Add(self, a: int, b: int) -> int: ...


class _GuardedImpl:
    """Hosts the contract method `Add`, plus off-contract attributes a
    non-conforming wire peer must NOT be able to reach."""

    def __init__(self) -> None:
        self.stolen: str | None = None
        self.wiped = False

    async def Add(self, a: int, b: int) -> int:
        return a + b

    async def Steal(self) -> str:  # public, but NOT on the contract
        self.stolen = "api-key-1234"
        return f"exfiltrated: {self.stolen}"

    async def _wipe(self) -> str:  # private helper
        self.wiped = True
        return "wiped"


def _make_guarded() -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter, _GuardedImpl]:
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    impl = _GuardedImpl()
    conn = IpcConnection(reader, writer, callbacks={"IGuarded": (IGuarded, impl)})  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer, impl


async def test_contract_method_dispatches() -> None:
    conn, reader, writer, _impl = _make_guarded()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IGuarded", method_name="Add", parameters=["2", "3"], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == 5
    finally:
        await conn.aclose()


@pytest.mark.parametrize("method_name", ["Steal", "_wipe", "__init__", "__class__"])
async def test_off_contract_method_is_rejected(method_name: str) -> None:
    """A peer not using the guarded proxy can name any attribute. The server
    must reject everything not declared on the contract — public off-contract
    methods, private helpers, and dunders — with MethodNotFoundException,
    matching .NET's interface-only resolution. (Repro of the review finding.)"""
    conn, reader, writer, impl = _make_guarded()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IGuarded", method_name=method_name, parameters=[], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert resp.error is not None
        assert resp.error.type_name == "UiPath.Ipc.MethodNotFoundException"
        # The off-contract attribute was never invoked.
        assert impl.stolen is None and impl.wiped is False
    finally:
        await conn.aclose()


# --- one-way (-> None) fire-and-forget ------------------------------------

class IOneWay(ABC):
    @abstractmethod
    async def FireAndForget(self, tag: str) -> None: ...

    @abstractmethod
    async def Explode(self) -> None: ...


class _OneWayImpl:
    def __init__(self) -> None:
        self.seen: list[str] = []
        self.release = asyncio.Event()

    async def FireAndForget(self, tag: str) -> None:
        await self.release.wait()  # stay blocked so we can prove the early ack
        self.seen.append(tag)

    async def Explode(self) -> None:
        raise ValueError("one-way failures are logged, not returned")


def _make_one_way() -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter, _OneWayImpl]:
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    impl = _OneWayImpl()
    conn = IpcConnection(reader, writer, callbacks={"IOneWay": (IOneWay, impl)})  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer, impl


async def test_one_way_acks_immediately_then_runs_detached() -> None:
    conn, reader, writer, impl = _make_one_way()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IOneWay", method_name="FireAndForget", parameters=['"x"'], id="7",
        )))
        # The ack comes back even though the handler is still blocked.
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert resp.request_id == "7"
        assert resp.error is None
        assert resp.data == ""  # empty ack, like .NET Response.Success(req, "")
        assert impl.seen == []  # handler hasn't finished yet
        # Let the detached handler complete; its side effect appears after.
        impl.release.set()
        for _ in range(100):
            await asyncio.sleep(0.01)
            if impl.seen:
                break
        assert impl.seen == ["x"]
    finally:
        await conn.aclose()


async def test_one_way_handler_exception_is_logged_not_returned(caplog) -> None:
    conn, reader, writer, _impl = _make_one_way()
    try:
        with caplog.at_level(logging.ERROR, logger="uipath_ipc.client.connection"):
            reader.feed_data(_request_frame(Request(
                endpoint="IOneWay", method_name="Explode", parameters=[], id="1",
            )))
            frames = await _wait_for_frames(writer, count=1)
            resp = Response.from_json(frames[0][1].decode("utf-8"))
            # Success ack — the failure is NOT sent back to the caller.
            assert resp.error is None
            assert resp.data == ""
            # ...but it is logged.
            await asyncio.sleep(0.05)
        recs = [r for r in caplog.records if "one-way" in r.message]
        assert recs, "expected a one-way failure log record"
        # The handler's exception (type + message) must be captured, not just
        # a bare 'one-way failed' line.
        exc_info = recs[0].exc_info
        assert exc_info is not None and exc_info[0] is ValueError
        assert "one-way failures are logged, not returned" in str(exc_info[1])
    finally:
        await conn.aclose()


# --- outbound exception-chain fidelity ------------------------------------

class IChainer(ABC):
    @abstractmethod
    async def Chain(self) -> bool: ...

    @abstractmethod
    async def Forward(self) -> bool: ...


class _ChainerImpl:
    async def Chain(self) -> bool:
        raise ValueError("outer") from KeyError("root cause")

    async def Forward(self) -> bool:
        # A handler that let a reach-back RemoteException propagate.
        raise RemoteException(
            "upstream failed",
            type_name="System.IO.IOException",
            stack_trace="<remote stack>",
        )


def _make_chainer() -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter]:
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    conn = IpcConnection(reader, writer, callbacks={"IChainer": (IChainer, _ChainerImpl())})  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer


async def test_inner_exception_chain_is_sent() -> None:
    """`raise ValueError from KeyError` must cross the wire with the KeyError as
    the Error's inner_error, so the caller's RemoteException reproduces it."""
    conn, reader, writer = _make_chainer()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IChainer", method_name="Chain", parameters=[], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert resp.error is not None
        assert resp.error.type_name == "ValueError"
        assert "outer" in resp.error.message
        assert resp.error.inner_error is not None
        assert resp.error.inner_error.type_name == "KeyError"
        assert "root cause" in resp.error.inner_error.message
    finally:
        await conn.aclose()


async def test_reraised_remote_exception_preserves_original_type() -> None:
    """A handler that re-raises a RemoteException must forward its original
    type/message/stack verbatim, not collapse to type_name='RemoteException'."""
    conn, reader, writer = _make_chainer()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="IChainer", method_name="Forward", parameters=[], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert resp.error is not None
        assert resp.error.type_name == "System.IO.IOException"
        assert resp.error.message == "upstream failed"
        assert resp.error.stack_trace == "<remote stack>"
    finally:
        await conn.aclose()


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
