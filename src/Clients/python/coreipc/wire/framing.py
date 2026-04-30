from __future__ import annotations

import asyncio
import struct

from .messages import MessageType

HEADER_LENGTH = 5
DEFAULT_MAX_MESSAGE_SIZE = 2 * 1024 * 1024

_HEADER = struct.Struct("<BI")


class FrameTooLargeError(Exception):
    pass


async def read_frame(
    reader: asyncio.StreamReader,
    *,
    max_message_size: int = DEFAULT_MAX_MESSAGE_SIZE,
) -> tuple[MessageType, bytes]:
    header = await reader.readexactly(HEADER_LENGTH)
    msg_type_byte, payload_length = _HEADER.unpack(header)
    if payload_length > max_message_size:
        raise FrameTooLargeError(
            f"Incoming frame payload length {payload_length} exceeds max {max_message_size}"
        )
    payload = await reader.readexactly(payload_length) if payload_length else b""
    return MessageType(msg_type_byte), payload


async def write_frame(
    writer: asyncio.StreamWriter,
    msg_type: MessageType,
    payload: bytes,
    *,
    max_message_size: int = DEFAULT_MAX_MESSAGE_SIZE,
) -> None:
    if len(payload) > max_message_size:
        raise FrameTooLargeError(
            f"Outgoing frame payload length {len(payload)} exceeds max {max_message_size}"
        )
    writer.write(_HEADER.pack(int(msg_type), len(payload)))
    if payload:
        writer.write(payload)
    await writer.drain()
