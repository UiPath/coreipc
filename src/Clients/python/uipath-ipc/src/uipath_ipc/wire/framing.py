"""Wire framing: 5-byte header + payload over an asyncio stream.

Frame layout:

    +---------+-----------------+---------------------+
    | MsgType | PayloadLength   | PayloadBytes ...    |
    | uint8   | int32 LE        | (PayloadLength)     |
    +---------+-----------------+---------------------+

Total header size is 5 bytes. Payload is UTF-8 JSON for all message types
defined in `wire.messages.MessageType`.
"""

from __future__ import annotations

import asyncio
import struct
from typing import Protocol

from .messages import MessageType

_HEADER_FORMAT = "<Bi"  # little-endian: uint8 + int32
_HEADER_LEN = 5


class FrameWriter(Protocol):
    """Structural type for objects that can accept a frame.

    Both `asyncio.StreamWriter` and test fakes satisfy this implicitly.
    """

    def write(self, data: bytes) -> None: ...
    async def drain(self) -> None: ...


async def read_frame(reader: asyncio.StreamReader) -> tuple[MessageType, bytes]:
    """Read exactly one frame from the stream.

    Raises:
        asyncio.IncompleteReadError: the stream closed mid-frame.
        ValueError: the message-type byte does not match a known `MessageType`.
    """
    header = await reader.readexactly(_HEADER_LEN)
    msg_type_byte, payload_len = struct.unpack(_HEADER_FORMAT, header)
    payload = await reader.readexactly(payload_len) if payload_len > 0 else b""
    return MessageType(msg_type_byte), payload


async def write_frame(
    writer: FrameWriter, msg_type: MessageType, payload: bytes
) -> None:
    """Write one frame to the stream and await drain."""
    header = struct.pack(_HEADER_FORMAT, int(msg_type), len(payload))
    writer.write(header + payload)
    await writer.drain()
