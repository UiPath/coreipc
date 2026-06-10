"""End-to-end tests for IpcServer.

These spin up a real Python server and call it from a real Python client
over a real transport (TCP loopback, and named pipe on supporting loops).
The per-request dispatch logic itself is unit-tested in
``tests/client/test_callbacks.py`` — here we prove the listen/accept layer
and the full client↔server round trip.
"""

from __future__ import annotations

import asyncio
import sys
import uuid
from abc import ABC, abstractmethod

import pytest

from uipath_ipc import (
    IpcClient,
    IpcServer,
    Message,
    NamedPipeClientTransport,
    NamedPipeServerTransport,
    RemoteException,
    TcpClientTransport,
    TcpServerTransport,
)


# --- example contract + service impl --------------------------------------

class ICalculator(ABC):
    @abstractmethod
    async def Add(self, a: float, b: float) -> float: ...

    @abstractmethod
    async def Concat(self, a: str, b: str) -> str: ...

    @abstractmethod
    async def Noop(self) -> None: ...

    @abstractmethod
    async def Fail(self) -> None: ...


class Calculator:
    """Note: does NOT inherit ICalculator — services are duck-typed."""

    def __init__(self) -> None:
        self.calls: list[tuple] = []

    async def Add(self, a: float, b: float) -> float:
        self.calls.append(("Add", a, b))
        return a + b

    async def Concat(self, a: str, b: str) -> str:
        return a + b

    async def Noop(self) -> None:
        self.calls.append(("Noop",))
        return None

    async def Fail(self) -> None:
        raise ValueError("kaboom")


# --- helpers --------------------------------------------------------------

async def _wait_until(predicate, timeout: float = 5.0) -> None:
    deadline = asyncio.get_running_loop().time() + timeout
    while not predicate():
        if asyncio.get_running_loop().time() > deadline:
            pytest.fail("condition not met within timeout")
        await asyncio.sleep(0.01)


def _tcp_endpoint(server: IpcServer) -> tuple[str, int]:
    """Read back the actually-bound (host, port) from a started TCP server."""
    assert server.handle is not None
    return server.handle.sockets[0].getsockname()[:2]  # type: ignore[attr-defined]


def _skip_if_no_pipe_support() -> None:
    loop = asyncio.get_running_loop()
    if sys.platform == "win32" and not hasattr(loop, "start_serving_pipe"):
        pytest.skip("event loop is not a ProactorEventLoop; pipes unsupported")


# --- TCP loopback ---------------------------------------------------------

async def test_tcp_client_calls_server_hosted_service() -> None:
    calc = Calculator()
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {ICalculator: calc})
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(ICalculator)
            assert await asyncio.wait_for(svc.Add(2.0, 3.0), timeout=5) == 5.0
            assert await asyncio.wait_for(svc.Concat("a", "b"), timeout=5) == "ab"
    assert ("Add", 2.0, 3.0) in calc.calls


async def test_tcp_void_method_returns_none() -> None:
    calc = Calculator()
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {ICalculator: calc})
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(ICalculator)
            assert await asyncio.wait_for(svc.Noop(), timeout=5) is None
    assert ("Noop",) in calc.calls


async def test_tcp_server_handler_exception_propagates_to_client() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {ICalculator: Calculator()})
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(ICalculator)
            with pytest.raises(RemoteException) as ei:
                await asyncio.wait_for(svc.Fail(), timeout=5)
            assert ei.value.type_name == "ValueError"
            assert "kaboom" in ei.value.message


async def test_tcp_multiple_concurrent_clients() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {ICalculator: Calculator()})
    async with server:
        host, port = _tcp_endpoint(server)

        async def one(n: int) -> float:
            async with IpcClient(TcpClientTransport(host, port)) as client:
                svc = client.get_proxy(ICalculator)
                return await asyncio.wait_for(svc.Add(float(n), float(n)), timeout=5)

        results = await asyncio.gather(*(one(i) for i in range(5)))
        assert results == [0.0, 2.0, 4.0, 6.0, 8.0]


async def test_tcp_connection_count_tracks_clients() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {ICalculator: Calculator()})
    async with server:
        host, port = _tcp_endpoint(server)
        assert server.connection_count == 0
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(ICalculator)
            await asyncio.wait_for(svc.Add(1.0, 1.0), timeout=5)
            await _wait_until(lambda: server.connection_count == 1)
        # Client disconnected → server prunes the connection via close callback.
        await _wait_until(lambda: server.connection_count == 0)


# --- lifecycle ------------------------------------------------------------

async def test_start_is_idempotent() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {})
    try:
        await server.start()
        handle = server.handle
        await server.start()
        assert server.handle is handle  # no second listener
    finally:
        await server.aclose()


async def test_serve_forever_returns_after_aclose() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {})
    await server.start()
    serving = asyncio.create_task(server.serve_forever())
    await asyncio.sleep(0)
    await server.aclose()
    await asyncio.wait_for(serving, timeout=5)


async def test_serve_forever_before_start_raises() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {})
    with pytest.raises(RuntimeError):
        await server.serve_forever()


async def test_serve_forever_blocks_for_named_pipe_until_aclose() -> None:
    """Regression: a named-pipe ServerHandle's wait_closed() must block, so
    serve_forever() doesn't return immediately and tear the server down."""
    _skip_if_no_pipe_support()
    name = f"uipath-ipc-srvtest-{uuid.uuid4().hex}"
    server = IpcServer(NamedPipeServerTransport(name), {})
    await server.start()
    serving = asyncio.create_task(server.serve_forever())
    await asyncio.sleep(0.05)
    assert not serving.done()  # must still be blocking while the listener is up
    await server.aclose()
    await asyncio.wait_for(serving, timeout=5)


async def test_aclose_closes_live_connections() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {ICalculator: Calculator()})
    await server.start()
    host, port = _tcp_endpoint(server)
    client = IpcClient(TcpClientTransport(host, port))
    svc = client.get_proxy(ICalculator)
    await asyncio.wait_for(svc.Add(1.0, 1.0), timeout=5)
    await _wait_until(lambda: server.connection_count == 1)
    await server.aclose()
    assert server.connection_count == 0
    assert server.handle is None
    await client.aclose()


# --- handler-initiated reach-back (Message.client.get_callback) -----------

class IGreeter(ABC):
    @abstractmethod
    async def GreetVia(self, name: str) -> str: ...


class IClientName(ABC):
    """Hosted by the *client*; the server's handler calls it back."""

    @abstractmethod
    async def Decorate(self, name: str) -> str: ...


class GreeterService:
    """Server-hosted; reaches back into the calling client mid-request."""

    async def GreetVia(self, name: str, m: Message) -> str:
        peer = m.client.get_callback(IClientName)
        decorated = await peer.Decorate(name)
        return f"hello {decorated}"


class ClientNameImpl:
    def __init__(self) -> None:
        self.calls: list[str] = []

    async def Decorate(self, name: str) -> str:
        self.calls.append(name)
        return name.upper()


async def test_server_handler_reaches_back_into_client_callback() -> None:
    """Full duplex re-entrancy: client → server → (callback) client → server."""
    impl = ClientNameImpl()
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {IGreeter: GreeterService()})
    async with server:
        host, port = _tcp_endpoint(server)
        client = IpcClient(
            TcpClientTransport(host, port), callbacks={IClientName: impl}
        )
        async with client:
            svc = client.get_proxy(IGreeter)
            result = await asyncio.wait_for(svc.GreetVia("bob"), timeout=5)
            assert result == "hello BOB"
            assert impl.calls == ["bob"]


# --- named pipe loopback --------------------------------------------------

async def test_named_pipe_client_calls_server_hosted_service() -> None:
    _skip_if_no_pipe_support()
    name = f"uipath-ipc-srvtest-{uuid.uuid4().hex}"
    calc = Calculator()
    server = IpcServer(NamedPipeServerTransport(name), {ICalculator: calc})
    async with server:
        async with IpcClient(NamedPipeClientTransport(name)) as client:
            svc = client.get_proxy(ICalculator)
            assert await asyncio.wait_for(svc.Add(10.0, 5.0), timeout=5) == 15.0
            assert await asyncio.wait_for(svc.Concat("x", "y"), timeout=5) == "xy"
    assert ("Add", 10.0, 5.0) in calc.calls


# --- transport construction -----------------------------------------------

def test_tcp_server_transport_stores_host_and_port() -> None:
    t = TcpServerTransport("127.0.0.1", 0)
    assert t.host == "127.0.0.1"
    assert t.port == 0


def test_named_pipe_server_transport_addresses() -> None:
    t = NamedPipeServerTransport("calc")
    assert t._windows_address == r"\\.\pipe\calc"
    assert t._posix_address == "/tmp/CoreFxPipe_calc"


def test_server_transports_are_immutable() -> None:
    with pytest.raises(Exception):
        TcpServerTransport("127.0.0.1", 0).port = 1  # type: ignore[misc]
    with pytest.raises(Exception):
        NamedPipeServerTransport("calc").pipe_name = "x"  # type: ignore[misc]
