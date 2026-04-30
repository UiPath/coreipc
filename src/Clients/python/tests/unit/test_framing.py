import asyncio
import socket

import pytest

from coreipc.wire.framing import (
    DEFAULT_MAX_MESSAGE_SIZE,
    FrameTooLargeError,
    HEADER_LENGTH,
    read_frame,
    write_frame,
)
from coreipc.wire.messages import MessageType


def test_header_length_matches_csharp():
    assert HEADER_LENGTH == 5


async def _stream_pair():
    rsock, wsock = socket.socketpair()
    loop = asyncio.get_running_loop()
    reader = asyncio.StreamReader()
    await loop.connect_accepted_socket(lambda: asyncio.StreamReaderProtocol(reader), rsock)
    transport, proto = await loop.create_connection(
        lambda: asyncio.StreamReaderProtocol(asyncio.StreamReader()), sock=wsock
    )
    writer = asyncio.StreamWriter(transport, proto, None, loop)
    return reader, writer


@pytest.mark.parametrize(
    "msg_type,payload",
    [
        pytest.param(MessageType.Request, b"{}", id="small-request"),
        pytest.param(MessageType.Response, b"", id="empty-response"),
        pytest.param(MessageType.CancellationRequest, b"x" * 1024, id="1k-cancel"),
        pytest.param(MessageType.Request, b"a" * 123456, id="large-request"),
    ],
)
async def test_round_trip(msg_type, payload):
    reader, writer = await _stream_pair()
    await write_frame(writer, msg_type, payload)
    mt, out = await read_frame(reader)
    assert mt == msg_type
    assert out == payload
    writer.close()


async def test_rejects_oversize_on_write():
    reader, writer = await _stream_pair()
    big = b"x" * (DEFAULT_MAX_MESSAGE_SIZE + 1)
    with pytest.raises(FrameTooLargeError):
        await write_frame(writer, MessageType.Request, big)
    writer.close()


async def test_rejects_oversize_on_read():
    reader, writer = await _stream_pair()
    # Bypass the write-side guard by writing raw bytes.
    from coreipc.wire.framing import _HEADER

    writer.write(_HEADER.pack(int(MessageType.Request), DEFAULT_MAX_MESSAGE_SIZE + 1))
    await writer.drain()
    with pytest.raises(FrameTooLargeError):
        await read_frame(reader)
    writer.close()
