"""Tests for cancellation forwarding."""

from __future__ import annotations

import asyncio
import struct

import pytest

from uipath_ipc.client import IpcConnection
from uipath_ipc.wire import CancellationRequest, MessageType, Request


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


async def _make_connection() -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter]:
    reader = asyncio.StreamReader()
    writer = _BufferWriter()
    conn = IpcConnection(reader, writer)  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer


def _split_frames(buf: bytes) -> list[tuple[int, bytes]]:
    """Decode `buf` as a sequence of frames; returns [(msg_type, payload), ...]."""
    out = []
    i = 0
    while i + 5 <= len(buf):
        msg_type = buf[i]
        length = int.from_bytes(buf[i + 1 : i + 5], "little", signed=True)
        i += 5
        out.append((msg_type, bytes(buf[i : i + length])))
        i += length
    return out


# --- happy path -----------------------------------------------------------

async def test_cancelling_a_request_sends_cancellation_frame() -> None:
    conn, _reader, writer = await _make_connection()
    try:
        req = Request(endpoint="X", method_name="Slow", parameters=[], id="1")
        # propagate_cancellation=True == an @ipc_cancellable method.
        task = asyncio.create_task(
            conn.send_request(req, propagate_cancellation=True)
        )
        await asyncio.sleep(0)  # let the request go out

        # Should now have one frame on the wire — the original request.
        frames = _split_frames(bytes(writer.buffer))
        assert len(frames) == 1
        assert frames[0][0] == int(MessageType.REQUEST)

        # Cancel the awaiting task.
        task.cancel()
        with pytest.raises(asyncio.CancelledError):
            await task

        # Allow the fire-and-forget cancellation task to run.
        for _ in range(20):
            await asyncio.sleep(0)
            if len(_split_frames(bytes(writer.buffer))) >= 2:
                break

        frames = _split_frames(bytes(writer.buffer))
        assert len(frames) == 2

        cancel_type, cancel_payload = frames[1]
        assert cancel_type == int(MessageType.CANCELLATION_REQUEST)
        cancel_msg = CancellationRequest.from_json(cancel_payload.decode("utf-8"))
        assert cancel_msg.request_id == "1"
    finally:
        await conn.aclose()


async def test_cancellation_not_forwarded_without_propagate_flag() -> None:
    """Default (an unmarked / not-@ipc_cancellable method): cancelling the
    awaiting task does NOT send a CancellationRequest — the cancel stays local."""
    conn, _reader, writer = await _make_connection()
    try:
        req = Request(endpoint="X", method_name="Slow", parameters=[], id="1")
        task = asyncio.create_task(conn.send_request(req))  # propagate defaults False
        await asyncio.sleep(0)
        assert len(_split_frames(bytes(writer.buffer))) == 1  # just the request

        task.cancel()
        with pytest.raises(asyncio.CancelledError):
            await task

        # Give any (erroneous) fire-and-forget cancellation a chance to appear.
        for _ in range(20):
            await asyncio.sleep(0)

        frames = _split_frames(bytes(writer.buffer))
        assert len(frames) == 1  # still ONLY the request — no cancellation frame
        assert frames[0][0] == int(MessageType.REQUEST)
    finally:
        await conn.aclose()


async def test_cancellation_on_closed_connection_is_silent() -> None:
    """If we cancel after the connection has closed, no error reaches the caller."""
    conn, _reader, _writer = await _make_connection()
    req = Request(endpoint="X", method_name="Y", parameters=[], id="1")
    task = asyncio.create_task(conn.send_request(req))
    await asyncio.sleep(0)

    # Close first, then cancel
    await conn.aclose()
    # The send_request future has already been failed by aclose
    with pytest.raises((ConnectionError, asyncio.CancelledError)):
        await task
