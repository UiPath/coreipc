"""Round-trip tests for wire/framing."""

from __future__ import annotations

import asyncio

import pytest

from uipath_ipc.wire import (
    MessageType,
    Request,
    read_frame,
    write_frame,
)


class _BufferWriter:
    """Fake StreamWriter that just collects bytes."""

    def __init__(self) -> None:
        self.buffer = bytearray()

    def write(self, data: bytes) -> None:
        self.buffer.extend(data)

    async def drain(self) -> None:
        pass


def _make_reader(data: bytes) -> asyncio.StreamReader:
    reader = asyncio.StreamReader()
    reader.feed_data(data)
    reader.feed_eof()
    return reader


# --- happy path -----------------------------------------------------------

async def test_round_trip_request_frame() -> None:
    payload = Request(
        endpoint="ISystemService",
        method_name="EchoString",
        parameters=['"hi"'],
    ).to_json().encode("utf-8")

    fw = _BufferWriter()
    await write_frame(fw, MessageType.REQUEST, payload)

    reader = _make_reader(bytes(fw.buffer))
    msg_type, got_payload = await read_frame(reader)

    assert msg_type == MessageType.REQUEST
    assert got_payload == payload


async def test_round_trip_empty_payload() -> None:
    """A frame with a zero-length payload is valid (e.g. an ack)."""
    fw = _BufferWriter()
    await write_frame(fw, MessageType.RESPONSE, b"")

    reader = _make_reader(bytes(fw.buffer))
    msg_type, payload = await read_frame(reader)

    assert msg_type == MessageType.RESPONSE
    assert payload == b""


async def test_header_layout_is_uint8_plus_int32_le() -> None:
    """Header is exactly 5 bytes: [type:uint8][len:int32 LE]."""
    fw = _BufferWriter()
    await write_frame(fw, MessageType.REQUEST, b"ab")  # 2-byte payload

    # Type byte = 0x00, length = 2 = 0x02 0x00 0x00 0x00 (LE)
    assert bytes(fw.buffer[:5]) == bytes([0x00, 0x02, 0x00, 0x00, 0x00])
    assert bytes(fw.buffer[5:]) == b"ab"


async def test_back_to_back_frames() -> None:
    """Reader should be able to consume multiple frames in a row."""
    fw = _BufferWriter()
    await write_frame(fw, MessageType.REQUEST, b"first")
    await write_frame(fw, MessageType.RESPONSE, b"second")

    reader = _make_reader(bytes(fw.buffer))
    t1, p1 = await read_frame(reader)
    t2, p2 = await read_frame(reader)

    assert (t1, p1) == (MessageType.REQUEST, b"first")
    assert (t2, p2) == (MessageType.RESPONSE, b"second")


# --- error paths ----------------------------------------------------------

async def test_read_fails_on_truncated_header() -> None:
    reader = _make_reader(b"\x00\x02")  # only 2 bytes
    with pytest.raises(asyncio.IncompleteReadError):
        await read_frame(reader)


async def test_read_fails_on_truncated_payload() -> None:
    # Valid header claiming 10 bytes, but only 3 follow
    reader = _make_reader(b"\x00\x0a\x00\x00\x00" + b"abc")
    with pytest.raises(asyncio.IncompleteReadError):
        await read_frame(reader)


async def test_read_fails_on_unknown_message_type() -> None:
    reader = _make_reader(b"\xff\x00\x00\x00\x00")  # type=255, empty payload
    with pytest.raises(ValueError):
        await read_frame(reader)
