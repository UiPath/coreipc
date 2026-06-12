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

#: Upper bound on a frame's payload, matching .NET's 2 MB server default
#: (MaxReceivedMessageSizeInMegabytes). The length field is a signed int32
#: from the peer — without a bound, a corrupt/hostile header could demand a
#: ~2 GB allocation, and a negative length would silently desync the stream.
MAX_PAYLOAD_BYTES = 2 * 1024 * 1024


class FrameWriter(Protocol):
    """Structural type for objects that can accept a frame.

    Both `asyncio.StreamWriter` and test fakes satisfy this implicitly.
    """

    def write(self, data: bytes) -> None: ...
    async def drain(self) -> None: ...


async def read_frame(
    reader: asyncio.StreamReader, max_payload: int = MAX_PAYLOAD_BYTES
) -> tuple[MessageType, bytes]:
    """Read exactly one frame from the stream.

    Raises:
        asyncio.IncompleteReadError: the stream closed mid-frame.
        ValueError: the message-type byte does not match a known `MessageType`,
            or the payload length is negative or exceeds ``max_payload``.
    """
    header = await reader.readexactly(_HEADER_LEN)
    msg_type_byte, payload_len = struct.unpack(_HEADER_FORMAT, header)
    if not 0 <= payload_len <= max_payload:
        raise ValueError(
            f"frame payload length {payload_len} out of bounds (max {max_payload})"
        )
    payload = await reader.readexactly(payload_len) if payload_len > 0 else b""
    return MessageType(msg_type_byte), payload


async def write_frame(
    writer: FrameWriter, msg_type: MessageType, payload: bytes
) -> None:
    """Write one frame to the stream and await drain."""
    header = struct.pack(_HEADER_FORMAT, int(msg_type), len(payload))
    writer.write(header + payload)
    await writer.drain()
