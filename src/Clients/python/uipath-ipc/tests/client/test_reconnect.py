"""Tests for auto-reconnect after a transport-level disconnect."""

from __future__ import annotations

import asyncio
import struct
from abc import ABC, abstractmethod

import pytest

from uipath_ipc import IpcClient
from uipath_ipc.transport.base import ClientTransport
from uipath_ipc.wire import MessageType, Response


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


class _ScriptedTransport(ClientTransport):
    """Hand out a pre-built (reader, writer) pair on each connect() call."""

    def __init__(self) -> None:
        self.connections: list[tuple[asyncio.StreamReader, _BufferWriter]] = []
        self.connect_calls = 0

    def add_connection(self) -> tuple[asyncio.StreamReader, _BufferWriter]:
        reader = asyncio.StreamReader()
        writer = _BufferWriter()
        self.connections.append((reader, writer))
        return reader, writer

    async def connect(self):  # type: ignore[override]
        if self.connect_calls >= len(self.connections):
            raise ConnectionError("no more scripted connections")
        pair = self.connections[self.connect_calls]
        self.connect_calls += 1
        return pair  # type: ignore[return-value]


def _response_frame(resp: Response) -> bytes:
    payload = resp.to_json().encode("utf-8")
    return struct.pack("<Bi", int(MessageType.RESPONSE), len(payload)) + payload


class IComputingService(ABC):
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...


# --- happy path ----------------------------------------------------------

async def test_second_call_redials_after_disconnect() -> None:
    t = _ScriptedTransport()
    pair1 = t.add_connection()
    pair2 = t.add_connection()

    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)

        # Call 1 — uses connection 1
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)
        pair1[0].feed_data(_response_frame(Response(request_id="1", data="3.0")))
        assert await asyncio.wait_for(task, timeout=1.0) == 3.0

        # Simulate server dropping connection 1
        pair1[0].feed_eof()
        # Let the receive loop notice and mark closed
        for _ in range(20):
            await asyncio.sleep(0)
            if client._connection is not None and client._connection.is_closed:
                break
        assert client._connection is not None and client._connection.is_closed

        # Call 2 — should redial via connection 2
        task = asyncio.create_task(svc.AddFloats(10.0, 20.0))
        await asyncio.sleep(0)
        # The new connection's writer should have the request
        assert len(pair2[1].buffer) > 0, "expected redial to use the second pair"
        pair2[0].feed_data(_response_frame(Response(request_id="1", data="30.0")))
        assert await asyncio.wait_for(task, timeout=1.0) == 30.0

        assert t.connect_calls == 2


async def test_id_counter_restarts_per_connection() -> None:
    """A fresh connection means a fresh id counter (starts at 1)."""
    t = _ScriptedTransport()
    pair1 = t.add_connection()
    pair2 = t.add_connection()

    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)

        # Call 1 — id will be "1"
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)
        pair1[0].feed_data(_response_frame(Response(request_id="1", data="3.0")))
        await asyncio.wait_for(task, timeout=1.0)

        # Drop
        pair1[0].feed_eof()
        for _ in range(20):
            await asyncio.sleep(0)
            if client._connection is not None and client._connection.is_closed:
                break

        # Call 2 — id should be "1" again on the new connection
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)
        # The new request is on pair2's writer; first frame is the new Request with id=1
        import json
        msg_type = pair2[1].buffer[0]
        payload_len = int.from_bytes(pair2[1].buffer[1:5], "little", signed=True)
        req = json.loads(pair2[1].buffer[5:5 + payload_len].decode("utf-8"))
        assert req["Id"] == "1"

        pair2[0].feed_data(_response_frame(Response(request_id="1", data="3.0")))
        await asyncio.wait_for(task, timeout=1.0)


async def test_in_flight_call_fails_when_connection_drops() -> None:
    """An in-flight call sees the underlying exception, not a silent retry."""
    t = _ScriptedTransport()
    pair1 = t.add_connection()

    async with IpcClient(t) as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.AddFloats(1.0, 2.0))
        await asyncio.sleep(0)

        # Drop the connection mid-call
        pair1[0].feed_eof()

        with pytest.raises(asyncio.IncompleteReadError):
            await asyncio.wait_for(task, timeout=1.0)
