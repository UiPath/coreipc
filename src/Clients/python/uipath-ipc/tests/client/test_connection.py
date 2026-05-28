"""Unit tests for IpcConnection using a fake stream pair."""

from __future__ import annotations

import asyncio
import struct

import pytest

from uipath_ipc.client import IpcConnection
from uipath_ipc.wire import MessageType, Request, Response


class _BufferWriter:
    """Stand-in for asyncio.StreamWriter — just accumulates bytes."""

    def __init__(self) -> None:
        self.buffer = bytearray()
        self._closed = False

    def write(self, data: bytes) -> None:
        self.buffer.extend(data)

    async def drain(self) -> None:
        pass

    def close(self) -> None:
        self._closed = True

    async def wait_closed(self) -> None:
        pass


def _frame(msg_type: MessageType, payload: bytes) -> bytes:
    return struct.pack("<Bi", int(msg_type), len(payload)) + payload


def _response_frame(resp: Response) -> bytes:
    return _frame(MessageType.RESPONSE, resp.to_json().encode("utf-8"))


async def _make_connection(*, prefeed: bytes = b"") -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter]:
    reader = asyncio.StreamReader()
    if prefeed:
        reader.feed_data(prefeed)
    writer = _BufferWriter()
    conn = IpcConnection(reader, writer)  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer


# --- happy path -----------------------------------------------------------

async def test_send_one_request_and_get_response() -> None:
    conn, reader, _writer = await _make_connection()
    try:
        send_task = asyncio.create_task(
            conn.send_request(Request(
                endpoint="X", method_name="Y", parameters=[], id="1",
            ))
        )

        # Let send_request register the future, then deliver the response.
        await asyncio.sleep(0)
        reader.feed_data(_response_frame(Response(request_id="1", data="42")))

        resp = await asyncio.wait_for(send_task, timeout=1.0)
        assert resp == Response(request_id="1", data="42")
    finally:
        await conn.aclose()


async def test_concurrent_requests_resolved_out_of_order() -> None:
    conn, reader, _writer = await _make_connection()
    try:
        t1 = asyncio.create_task(
            conn.send_request(Request(endpoint="X", method_name="Y", parameters=[], id="1"))
        )
        t2 = asyncio.create_task(
            conn.send_request(Request(endpoint="X", method_name="Z", parameters=[], id="2"))
        )
        await asyncio.sleep(0)
        # Deliver response for id=2 first, then id=1
        reader.feed_data(_response_frame(Response(request_id="2", data="second")))
        reader.feed_data(_response_frame(Response(request_id="1", data="first")))

        r1 = await asyncio.wait_for(t1, timeout=1.0)
        r2 = await asyncio.wait_for(t2, timeout=1.0)
        assert r1.data == "first"
        assert r2.data == "second"
    finally:
        await conn.aclose()


# --- failure paths --------------------------------------------------------

async def test_stream_close_fails_pending_requests() -> None:
    conn, reader, _writer = await _make_connection()
    try:
        send_task = asyncio.create_task(
            conn.send_request(Request(endpoint="X", method_name="Y", parameters=[], id="1"))
        )
        await asyncio.sleep(0)

        # Simulate stream close mid-request — the receive loop hits IncompleteReadError.
        reader.feed_eof()

        with pytest.raises(asyncio.IncompleteReadError):
            await asyncio.wait_for(send_task, timeout=1.0)
    finally:
        await conn.aclose()


async def test_send_on_closed_connection_raises() -> None:
    conn, _reader, _writer = await _make_connection()
    await conn.aclose()
    with pytest.raises(ConnectionError):
        await conn.send_request(Request(endpoint="X", method_name="Y", parameters=[], id="1"))


# --- request id allocation ------------------------------------------------

async def test_next_id_increments() -> None:
    conn, _reader, _writer = await _make_connection()
    try:
        assert conn.next_id() == "1"
        assert conn.next_id() == "2"
        assert conn.next_id() == "3"
    finally:
        await conn.aclose()


# --- bytes on the wire ----------------------------------------------------

async def test_wire_format_is_request_frame() -> None:
    conn, reader, writer = await _make_connection()
    try:
        req = Request(endpoint="ISystemService", method_name="EchoString",
                      parameters=['"hi"'], id="1")
        send_task = asyncio.create_task(conn.send_request(req))
        await asyncio.sleep(0)

        # Inspect what was written
        assert len(writer.buffer) > 5
        msg_type_byte = writer.buffer[0]
        payload_len = int.from_bytes(writer.buffer[1:5], "little", signed=True)
        assert msg_type_byte == int(MessageType.REQUEST)
        assert payload_len == len(writer.buffer) - 5

        # Tidy up: deliver a response so send_task can finish
        reader.feed_data(_response_frame(Response(request_id="1", data="ok")))
        await asyncio.wait_for(send_task, timeout=1.0)
    finally:
        await conn.aclose()
