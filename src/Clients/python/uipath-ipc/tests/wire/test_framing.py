"""Round-trip tests for wire/framing."""

from __future__ import annotations

import asyncio
import struct

import pytest

from uipath_ipc.wire import (
    MessageType,
    Request,
    read_frame,
    write_frame,
)
from uipath_ipc.wire.framing import MAX_PAYLOAD_BYTES


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


async def test_read_fails_on_negative_payload_length() -> None:
    """A negative int32 length would silently desync the stream."""
    reader = _make_reader(struct.pack("<Bi", 0, -1))
    with pytest.raises(ValueError, match="out of bounds"):
        await read_frame(reader)


async def test_read_fails_on_oversized_payload_length() -> None:
    """A length beyond the 2 MB cap (matching .NET's server default) must be
    rejected BEFORE allocating — a hostile header could claim ~2 GB."""
    reader = _make_reader(struct.pack("<Bi", 0, MAX_PAYLOAD_BYTES + 1))
    with pytest.raises(ValueError, match="out of bounds"):
        await read_frame(reader)


async def test_read_accepts_payload_at_exact_cap_boundary() -> None:
    payload = b"x" * 16
    reader = _make_reader(struct.pack("<Bi", 0, len(payload)) + payload)
    msg_type, got = await read_frame(reader, max_payload=16)  # len == cap
    assert (msg_type, got) == (MessageType.REQUEST, payload)
