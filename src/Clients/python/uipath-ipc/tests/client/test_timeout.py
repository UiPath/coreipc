"""Tests for client-side request timeouts."""

from __future__ import annotations

import asyncio
import json
import struct
from abc import ABC, abstractmethod

import pytest

from uipath_ipc import IpcClient
from uipath_ipc.transport.base import ClientTransport
from uipath_ipc.wire import CancellationRequest, MessageType, Response


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


class _FakeTransport(ClientTransport):
    def __init__(self) -> None:
        self.reader = asyncio.StreamReader()
        self.writer = _BufferWriter()

    async def connect(self):  # type: ignore[override]
        return self.reader, self.writer  # type: ignore[return-value]


def _response_frame(resp: Response) -> bytes:
    payload = resp.to_json().encode("utf-8")
    return struct.pack("<Bi", int(MessageType.RESPONSE), len(payload)) + payload


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


class IComputingService(ABC):
    @abstractmethod
    async def Wait(self, duration: float) -> bool: ...

    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...


# --- happy path ----------------------------------------------------------

async def test_request_timeout_raises_timeout_error() -> None:
    t = _FakeTransport()
    async with IpcClient(t, request_timeout=0.05) as client:
        svc = client.get_proxy(IComputingService)
        with pytest.raises(asyncio.TimeoutError):
            await svc.Wait(10.0)  # response never arrives → times out


async def test_timeout_sends_cancellation_to_server() -> None:
    t = _FakeTransport()
    async with IpcClient(t, request_timeout=0.05) as client:
        svc = client.get_proxy(IComputingService)
        with pytest.raises(asyncio.TimeoutError):
            await svc.Wait(10.0)

        # Allow the fire-and-forget cancellation task to run
        for _ in range(20):
            await asyncio.sleep(0)
            if len(_split_frames(bytes(t.writer.buffer))) >= 2:
                break

        frames = _split_frames(bytes(t.writer.buffer))
        # Frame 0 is the original Request, frame 1 should be the cancellation
        assert len(frames) == 2
        assert frames[0][0] == int(MessageType.REQUEST)
        assert frames[1][0] == int(MessageType.CANCELLATION_REQUEST)
        cancel = CancellationRequest.from_json(frames[1][1].decode("utf-8"))
        assert cancel.request_id == "1"


async def test_request_includes_timeout_in_seconds_field() -> None:
    t = _FakeTransport()
    async with IpcClient(t, request_timeout=2.5) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)

        frames = _split_frames(bytes(t.writer.buffer))
        req_payload = json.loads(frames[0][1].decode("utf-8"))
        assert req_payload["TimeoutInSeconds"] == 2.5

        # Tidy up
        t.reader.feed_data(_response_frame(Response(request_id="1", data="3.0")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_no_timeout_default_waits_indefinitely() -> None:
    """Without request_timeout set, a slow response simply isn't timed out
    by the client. We verify by polling briefly that the call is still pending."""
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0.05)
        assert not task.done()

        # Resolve so the test exits cleanly
        t.reader.feed_data(_response_frame(Response(request_id="1", data="3.0")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_request_timeout_in_seconds_field_is_zero_by_default() -> None:
    """No client-side timeout sends ``TimeoutInSeconds: 0`` on the wire.

    The .NET Request.TimeoutInSeconds is a non-nullable double, with 0 as the
    sentinel for 'no timeout, use the server's default'. Emitting null would
    make the .NET-side Newtonsoft.Json deserializer reject the whole request.
    """
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)

        frames = _split_frames(bytes(t.writer.buffer))
        req_payload = json.loads(frames[0][1].decode("utf-8"))
        assert req_payload["TimeoutInSeconds"] == 0

        # Tidy up
        t.reader.feed_data(_response_frame(Response(request_id="1", data="3.0")))
        await asyncio.wait_for(task, timeout=1.0)
