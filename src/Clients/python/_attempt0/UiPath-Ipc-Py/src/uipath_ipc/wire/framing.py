"""Message framing: 5-byte header read/write matching the .NET CoreIpc wire format.

Header: [MessageType: uint8][PayloadLength: int32_LE]
"""

from __future__ import annotations

import asyncio
import struct

from .dtos import MessageType

HEADER_LENGTH = 5  # 1 byte MessageType + 4 bytes int32 LE


async def write_message(
    writer: asyncio.StreamWriter,
    msg_type: MessageType,
    payload: bytes,
) -> None:
    """Write a framed message: [type:1][length:4][payload]."""
    header = struct.pack("<Bi", msg_type.value, len(payload))
    writer.write(header + payload)
    await writer.drain()


async def read_message(
    reader: asyncio.StreamReader,
) -> tuple[MessageType, bytes] | None:
    """Read a framed message. Returns None on connection close."""
    header = await _read_exactly(reader, HEADER_LENGTH)
    if header is None:
        return None
    msg_type = MessageType(header[0])
    length = struct.unpack("<i", header[1:5])[0]
    payload = await _read_exactly(reader, length)
    if payload is None:
        return None
    return (msg_type, payload)


async def _read_exactly(reader: asyncio.StreamReader, n: int) -> bytes | None:
    """Read exactly n bytes, returning None on EOF."""
    try:
        return await reader.readexactly(n)
    except (asyncio.IncompleteReadError, ConnectionError):
        return None
