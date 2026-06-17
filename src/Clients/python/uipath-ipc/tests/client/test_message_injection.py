"""Unit tests for `Message` injection and connection-bound `get_callback`.

These drive `IpcConnection` with a fake stream (like test_callbacks.py),
verifying the handler-side reach-back machinery in isolation. The full
bidirectional round trip over a real transport lives in
tests/server/test_ipc_server.py.
"""

from __future__ import annotations

import asyncio
import json
import struct

import pytest

from uipath_ipc.client import IpcConnection
from uipath_ipc.message import Message
from uipath_ipc.wire import MessageType, Request, Response


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


async def _wait_for_frames(
    writer: _BufferWriter, count: int, timeout: float = 1.0
) -> list[tuple[int, bytes]]:
    deadline = asyncio.get_running_loop().time() + timeout
    while True:
        frames = _split_frames(bytes(writer.buffer))
        if len(frames) >= count:
            return frames
        if asyncio.get_running_loop().time() > deadline:
            pytest.fail(f"only saw {len(frames)} frames; expected {count}")
        await asyncio.sleep(0.01)


# --- a service whose methods declare a Message parameter ------------------

class _Service:
    def __init__(self) -> None:
        self.messages: list[Message] = []

    async def Greet(self, name: str, m: Message) -> str:
        self.messages.append(m)
        return f"hi {name}"

    async def Ping(self, m: Message) -> bool:
        self.messages.append(m)
        return True

    async def NoMessage(self, x: int, y: int) -> int:
        return x + y

    async def KwOnlyMessage(self, value: str, *, m: Message) -> str:
        self.messages.append(m)
        return f"kw {value}"

    async def OptionalMessage(self, value: str, m: Message | None = None) -> str:
        self.messages.append(m)
        return f"opt {value}"

    async def Save(self, m: Message) -> bool:
        # The contract declares the Message, so the wire carries a slot for it
        # ({} or {"Payload": ...}); the handler must see the Payload.
        self.messages.append(m)
        return True

    async def Annotate(self, label: str, m: Message, count: int) -> str:
        # Non-trailing Message: its wire slot must be consumed so `count` stays
        # aligned with the parameter after it.
        self.messages.append(m)
        return f"{label}:{count}"


def _make_connection(
    svc: _Service,
) -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter]:
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    conn = IpcConnection(reader, writer, callbacks={"ISvc": (_Service, svc)})  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer


# --- injection ------------------------------------------------------------

async def test_message_is_injected_with_caller_connection() -> None:
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="Greet", parameters=['"bob"'], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))

        assert json.loads(resp.data) == "hi bob"
        assert len(svc.messages) == 1
        # The injected Message carries THIS connection as its client.
        assert svc.messages[0].client is conn
    finally:
        await conn.aclose()


async def test_message_only_param_consumes_no_wire_args() -> None:
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="Ping", parameters=[], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) is True
        assert svc.messages[0].client is conn
    finally:
        await conn.aclose()


async def test_request_timeout_flows_into_injected_message() -> None:
    svc = _Service()
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    conn = IpcConnection(
        reader, writer, callbacks={"ISvc": (_Service, svc)}, request_timeout=3.5  # type: ignore[arg-type]
    )
    conn.start()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="Ping", parameters=[], id="1",
        )))
        await _wait_for_frames(writer, count=1)
        assert svc.messages[0].request_timeout == 3.5
    finally:
        await conn.aclose()


async def test_handler_without_message_is_unaffected() -> None:
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="NoMessage", parameters=["3", "4"], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == 7
    finally:
        await conn.aclose()


async def test_keyword_only_message_is_injected() -> None:
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="KwOnlyMessage", parameters=['"hi"'], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == "kw hi"
        assert svc.messages[0].client is conn
    finally:
        await conn.aclose()


async def test_optional_message_annotation_is_injected() -> None:
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="OptionalMessage", parameters=['"hi"'], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == "opt hi"
        assert svc.messages[0] is not None
        assert svc.messages[0].client is conn
    finally:
        await conn.aclose()


# --- server before_incoming_call hook -------------------------------------

async def test_before_incoming_call_fires_before_dispatch() -> None:
    seen: list[object] = []

    async def hook(ci: object) -> None:
        seen.append(ci)

    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    svc = _Service()
    conn = IpcConnection(
        reader, writer, callbacks={"ISvc": (_Service, svc)}, before_incoming_call=hook  # type: ignore[arg-type]
    )
    conn.start()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="NoMessage", parameters=["3", "4"], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == 7
        assert len(seen) == 1
        assert seen[0].endpoint == "ISvc"  # type: ignore[attr-defined]
        assert seen[0].method_name == "NoMessage"  # type: ignore[attr-defined]
        assert seen[0].arguments == (3, 4)  # type: ignore[attr-defined]
    finally:
        await conn.aclose()


async def test_before_incoming_call_raising_aborts_with_error() -> None:
    async def hook(ci: object) -> None:
        raise ValueError("denied")

    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    conn = IpcConnection(
        reader, writer, callbacks={"ISvc": (_Service, _Service())}, before_incoming_call=hook  # type: ignore[arg-type]
    )
    conn.start()
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="NoMessage", parameters=["3", "4"], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert resp.error is not None
        assert "denied" in resp.error.message
    finally:
        await conn.aclose()


async def test_message_payload_is_read_from_its_wire_slot() -> None:
    """A contract-declared `Message` rides one wire slot ({} or {"Payload": …});
    the handler must receive the Payload, not lose it."""
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc",
            method_name="Save",
            parameters=['{"Payload": {"id": 5}}'],
            id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) is True
        assert svc.messages[0].payload == {"id": 5}
        assert svc.messages[0].client is conn
    finally:
        await conn.aclose()


async def test_non_trailing_message_keeps_later_args_aligned() -> None:
    """A `Message` between two value params must consume its own wire slot so
    the parameter after it isn't shifted (regression: payload dropped / arg
    drift)."""
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc",
            method_name="Annotate",
            parameters=['"label-text"', "{}", "42"],
            id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == "label-text:42"
        assert svc.messages[0].client is conn
    finally:
        await conn.aclose()


async def test_extra_trailing_wire_arg_is_ignored() -> None:
    """A .NET client serializes a trailing CancellationToken as "" — the extra
    wire parameter must be ignored, not bound to a handler parameter."""
    svc = _Service()
    conn, reader, writer = _make_connection(svc)
    try:
        reader.feed_data(_request_frame(Request(
            endpoint="ISvc", method_name="NoMessage", parameters=["3", "4", '""'], id="1",
        )))
        frames = await _wait_for_frames(writer, count=1)
        resp = Response.from_json(frames[0][1].decode("utf-8"))
        assert json.loads(resp.data) == 7
    finally:
        await conn.aclose()


# --- get_callback ---------------------------------------------------------

async def test_get_callback_sends_request_over_same_connection() -> None:
    """A reach-back proxy writes a REQUEST frame to this connection."""
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    conn = IpcConnection(reader, writer)  # type: ignore[arg-type]
    conn.start()
    try:
        class IPeer:
            async def DoThing(self, value: str) -> str: ...

        proxy = conn.get_callback(IPeer)
        task = asyncio.create_task(proxy.DoThing("hello"))
        frames = await _wait_for_frames(writer, count=1)

        msg_type, payload = frames[0]
        assert msg_type == int(MessageType.REQUEST)
        sent = Request.from_json(payload.decode("utf-8"))
        assert sent.endpoint == "IPeer"
        assert sent.method_name == "DoThing"
        assert sent.parameters == ['"hello"']  # arg JSON-encoded individually

        # Feed the matching RESPONSE so the outbound call completes.
        rp = Response(request_id=sent.id, data=json.dumps("done")).to_json().encode("utf-8")
        reader.feed_data(struct.pack("<Bi", int(MessageType.RESPONSE), len(rp)) + rp)
        assert await asyncio.wait_for(task, timeout=1.0) == "done"
    finally:
        await conn.aclose()
