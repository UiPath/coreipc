"""Unit tests for the @ipc_cancellable contract marker (markers.py).

The marker is documentation-only: it records that a method's .NET counterpart
ends with a CancellationToken. The load-bearing property is that it has ZERO
wire effect — in particular it must NOT append an empty-string slot (that would
corrupt optional-param alignment; see the design notes on the PR)."""

from __future__ import annotations

import asyncio
import json
import struct
from abc import ABC, abstractmethod

from uipath_ipc import IpcClient, ipc_cancellable
from uipath_ipc.markers import IPC_CANCELLABLE_ATTR
from uipath_ipc.transport.base import ClientTransport
from uipath_ipc.wire import MessageType, Response


# --- mechanics -------------------------------------------------------------

def test_marks_method_and_returns_same_object() -> None:
    def f(self, x: int) -> int: ...  # noqa: ANN001

    marked = ipc_cancellable(f)
    assert marked is f  # returns the same object — not a wrapper
    assert getattr(f, IPC_CANCELLABLE_ATTR) is True


def test_composes_with_abstractmethod_either_order() -> None:
    class Outer(ABC):
        @ipc_cancellable
        @abstractmethod
        async def Foo(self, x: int) -> int: ...

    class Inner(ABC):
        @abstractmethod
        @ipc_cancellable
        async def Bar(self, x: int) -> int: ...

    for cls, name in ((Outer, "Foo"), (Inner, "Bar")):
        method = getattr(cls, name)
        assert getattr(method, "__isabstractmethod__", False) is True
        assert getattr(method, IPC_CANCELLABLE_ATTR) is True


# --- zero wire effect ------------------------------------------------------

class _BufferWriter:
    def __init__(self) -> None:
        self.buffer = bytearray()

    def write(self, data: bytes) -> None:
        self.buffer.extend(data)

    async def drain(self) -> None: ...
    def close(self) -> None: ...
    async def wait_closed(self) -> None: ...


class _FakeTransport(ClientTransport):
    def __init__(self) -> None:
        self.reader = asyncio.StreamReader()
        self.writer = _BufferWriter()

    async def connect(self):  # type: ignore[override]
        return self.reader, self.writer  # type: ignore[return-value]


def _response_frame(resp: Response) -> bytes:
    payload = resp.to_json().encode("utf-8")
    return struct.pack("<Bi", int(MessageType.RESPONSE), len(payload)) + payload


def _first_request_params(buf: bytes) -> list[str]:
    # 5-byte header [uint8 type, int32 LE length] then UTF-8 JSON.
    length = int.from_bytes(buf[1:5], "little", signed=True)
    payload = json.loads(bytes(buf[5 : 5 + length]).decode("utf-8"))
    return payload["Parameters"]


class _IService(ABC):
    @ipc_cancellable
    @abstractmethod
    async def Cancellable(self, x: int) -> int: ...

    @abstractmethod
    async def Plain(self, x: int) -> int: ...


async def _params_for(method_name: str) -> list[str]:
    t = _FakeTransport()
    async with IpcClient(t) as client:
        svc = client.get_proxy(_IService)
        task = asyncio.create_task(getattr(svc, method_name)(7))
        for _ in range(50):
            await asyncio.sleep(0)
            if t.writer.buffer:
                break
        params = _first_request_params(bytes(t.writer.buffer))
        # Resolve so the client closes cleanly (first request id is "1").
        t.reader.feed_data(_response_frame(Response(request_id="1", data="42")))
        await asyncio.wait_for(task, timeout=1.0)
    return params


async def test_ipc_cancellable_adds_nothing_to_the_wire() -> None:
    # The annotated method serializes exactly its one declared arg — no extra
    # CancellationToken "" slot appended — identical to the unannotated method.
    cancellable_params = await _params_for("Cancellable")
    plain_params = await _params_for("Plain")
    assert cancellable_params == plain_params
    assert cancellable_params == [json.dumps(7)]  # just the one declared arg
