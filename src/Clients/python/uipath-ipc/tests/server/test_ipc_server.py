"""End-to-end tests for IpcServer.

These spin up a real Python server and call it from a real Python client
over a real transport (TCP loopback, and named pipe on supporting loops).
The per-request dispatch logic itself is unit-tested in
``tests/client/test_callbacks.py`` — here we prove the listen/accept layer
and the full client↔server round trip.
"""

from __future__ import annotations

import asyncio
import datetime as dt
import os
import socket
import stat
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
from uipath_ipc.transport.base import ServerTransport


# --- example contract + service impl --------------------------------------

class ICalculator(ABC):
    @abstractmethod
    async def Add(self, a: float, b: float) -> float: ...

    @abstractmethod
    async def Concat(self, a: str, b: str) -> str: ...

    @abstractmethod
    async def Noop(self) -> None: ...

    @abstractmethod
    async def Fail(self) -> bool: ...


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

    async def Fail(self) -> bool:
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
    """A `-> None` method is one-way: the client gets an immediate ack (None)
    and the handler runs detached server-side, so its side effect is observed
    shortly after the call returns rather than synchronously."""
    calc = Calculator()
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {ICalculator: calc})
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(ICalculator)
            assert await asyncio.wait_for(svc.Noop(), timeout=5) is None
            await _wait_until(lambda: ("Noop",) in calc.calls)


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


class _CountingServerTransport(ServerTransport):
    """Counts serve() calls and yields once mid-call to widen the start race."""

    def __init__(self) -> None:
        self.serve_calls = 0

    async def serve(self, on_connection):  # type: ignore[override]
        self.serve_calls += 1
        await asyncio.sleep(0)  # yield so a racing start() can interleave

        class _Handle:
            def close(self) -> None: ...
            async def wait_closed(self) -> None: ...

        return _Handle()


async def test_concurrent_start_binds_one_listener() -> None:
    """Racing start() calls must bind exactly one listener (the check-then-await
    gap would otherwise leak extras)."""
    transport = _CountingServerTransport()
    server = IpcServer(transport, {})
    try:
        await asyncio.gather(*(server.start() for _ in range(5)))
        assert transport.serve_calls == 1
        assert server.handle is not None
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


# --- server-side value-type (de)serialization -----------------------------

class IValueTypes(ABC):
    @abstractmethod
    async def RoundTripDateTime(self, when: dt.datetime) -> dt.datetime: ...

    @abstractmethod
    async def ReverseBytes(self, blob: bytes) -> bytes: ...

    @abstractmethod
    async def EchoGuid(self, value: uuid.UUID) -> uuid.UUID: ...


class ValueTypesService:
    async def RoundTripDateTime(self, when: dt.datetime) -> dt.datetime:
        assert isinstance(when, dt.datetime)  # decoded, not a raw ISO str
        return when

    async def ReverseBytes(self, blob: bytes) -> bytes:
        assert isinstance(blob, (bytes, bytearray))  # decoded, not base64 str
        return bytes(reversed(blob))

    async def EchoGuid(self, value: uuid.UUID) -> uuid.UUID:
        assert isinstance(value, uuid.UUID)
        return value


async def test_server_round_trips_value_types() -> None:
    """A Python *server* must decode value-type args (`from_wire`) and encode
    value-type returns (`to_wire`) — not just the client. Without it, a `bytes`
    parameter arrives as base64 str and a `datetime` return raises TypeError."""
    server = IpcServer(
        TcpServerTransport("127.0.0.1", 0), {IValueTypes: ValueTypesService()}
    )
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(IValueTypes)
            when = dt.datetime(2026, 6, 16, 10, 30, tzinfo=dt.timezone.utc)
            assert await asyncio.wait_for(svc.RoundTripDateTime(when), timeout=5) == when
            assert (
                await asyncio.wait_for(svc.ReverseBytes(b"\x01\x02\x03"), timeout=5)
                == b"\x03\x02\x01"
            )
            u = uuid.UUID("550e8400-e29b-41d4-a716-446655440000")
            assert await asyncio.wait_for(svc.EchoGuid(u), timeout=5) == u


# --- server-side request-timeout enforcement -------------------------------

class ISlow(ABC):
    @abstractmethod
    async def Slow(self) -> bool: ...


class SlowService:
    async def Slow(self) -> bool:
        await asyncio.sleep(5)
        return True


async def test_server_enforces_request_timeout() -> None:
    """A server with a configured request_timeout bounds an inbound handler:
    a 5s handler against a 0.2s server budget returns a TimeoutException even
    though the client set no timeout of its own."""
    server = IpcServer(
        TcpServerTransport("127.0.0.1", 0),
        {ISlow: SlowService()},
        request_timeout=0.2,
    )
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(ISlow)
            with pytest.raises(RemoteException) as ei:
                await asyncio.wait_for(svc.Slow(), timeout=5)
            assert ei.value.type_name == "System.TimeoutException"


# --- POSIX unix-socket lifecycle -------------------------------------------

@pytest.mark.skipif(sys.platform == "win32", reason="POSIX unix-socket lifecycle")
async def test_posix_server_removes_socket_file_on_close() -> None:
    """Closing a POSIX named-pipe server unlinks its socket file (like .NET's
    delete-on-dispose), instead of leaving a stale file behind."""
    name = f"uipath-ipc-life-{uuid.uuid4().hex}"
    transport = NamedPipeServerTransport(name)
    path = transport._posix_address
    server = IpcServer(transport, {ICalculator: Calculator()})
    await server.start()
    assert os.path.exists(path)
    await server.aclose()
    assert not os.path.exists(path)


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX unix-socket lifecycle")
async def test_posix_socket_is_owner_only_regardless_of_umask() -> None:
    """The socket is chmod'd to 0o600 after bind, so even a permissive umask
    can't leave it group/world-connectable (mirrors .NET's owner-only perms)."""
    name = f"uipath-ipc-perm-{uuid.uuid4().hex}"
    transport = NamedPipeServerTransport(name)
    path = transport._posix_address
    server = IpcServer(transport, {ICalculator: Calculator()})
    old_umask = os.umask(0o000)  # would otherwise bind the socket world-rw
    try:
        await server.start()
    finally:
        os.umask(old_umask)
    try:
        assert stat.S_IMODE(os.stat(path).st_mode) == 0o600
    finally:
        await server.aclose()


@pytest.mark.skipif(sys.platform == "win32", reason="POSIX unix-socket lifecycle")
async def test_posix_bind_over_live_server_raises() -> None:
    """A second server on the same name must NOT blindly unlink a LIVE server's
    socket (silent hijack) — the liveness probe makes it fail loudly instead."""
    name = f"uipath-ipc-live-{uuid.uuid4().hex}"
    server1 = IpcServer(NamedPipeServerTransport(name), {ICalculator: Calculator()})
    await server1.start()
    try:
        server2 = IpcServer(NamedPipeServerTransport(name), {ICalculator: Calculator()})
        with pytest.raises(OSError):
            await server2.start()
    finally:
        await server1.aclose()


# --- TCP connect resilience / zero-timeout edge ----------------------------

async def test_tcp_client_rides_out_connection_refused() -> None:
    """A client dialing before the server's accept loop binds must retry
    ConnectionRefused and ride out the startup race, not fail the first call."""
    # Reserve a free port, then release it so nothing is listening yet.
    probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    probe.bind(("127.0.0.1", 0))
    port = probe.getsockname()[1]
    probe.close()

    calc = Calculator()
    server = IpcServer(TcpServerTransport("127.0.0.1", port), {ICalculator: calc})
    client = IpcClient(TcpClientTransport("127.0.0.1", port))

    async def bind_late() -> None:
        await asyncio.sleep(0.15)  # within the retry ladder's window
        await server.start()

    starter = asyncio.create_task(bind_late())
    try:
        svc = client.get_proxy(ICalculator)
        assert await asyncio.wait_for(svc.Add(2.0, 3.0), timeout=5) == 5.0
    finally:
        await starter
        await client.aclose()
        await server.aclose()


class IBriefly(ABC):
    @abstractmethod
    async def Work(self, seconds: float) -> bool: ...


class BrieflyService:
    async def Work(self, seconds: float) -> bool:
        await asyncio.sleep(seconds)
        return True


async def test_zero_per_call_timeout_is_not_an_instant_deadline() -> None:
    """Message(request_timeout=0) means 'use the server default', not an instant
    client-side wait_for(0): a ~0.2s call completes instead of timing out."""
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {IBriefly: BrieflyService()})
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(IBriefly)
            assert await asyncio.wait_for(
                svc.Work(0.2, Message(request_timeout=0)), timeout=5
            ) is True


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


# --- before_connect: server lifecycle + self-healing ------------------------

async def test_before_connect_launches_and_self_heals_python_server() -> None:
    """The killer app of `before_connect`: the client owns its server's
    lifecycle. The hook lazily launches the server before the first connect,
    stays quiet while the connection is healthy, and — because it runs before
    every (re)connect — transparently relaunches the server after it
    disappears: the next ordinary call just succeeds (self-healing)."""
    _skip_if_no_pipe_support()
    name = f"uipath-ipc-heal-{uuid.uuid4().hex}"
    servers: list[IpcServer] = []
    launches = 0

    async def launch_server() -> None:
        nonlocal launches
        if servers and servers[-1].handle is not None:
            return  # server alive — nothing to do
        launches += 1
        srv = IpcServer(NamedPipeServerTransport(name), {ICalculator: Calculator()})
        await srv.start()
        servers.append(srv)

    client = IpcClient(NamedPipeClientTransport(name), before_connect=launch_server)
    try:
        svc = client.get_proxy(ICalculator)

        # First call: hook launches the server.
        assert await asyncio.wait_for(svc.Add(1.0, 2.0), timeout=5) == 3.0
        assert launches == 1

        # Healthy connection: hook does not refire.
        assert await asyncio.wait_for(svc.Add(2.0, 2.0), timeout=5) == 4.0
        assert launches == 1

        # The server disappears...
        await servers[0].aclose()
        await _wait_until(
            lambda: client._connection is not None and client._connection.is_closed
        )

        # ...and the next call self-heals: relaunch + transparent success.
        assert await asyncio.wait_for(svc.Add(3.0, 4.0), timeout=5) == 7.0
        assert launches == 2
    finally:
        await client.aclose()
        for srv in servers:
            await srv.aclose()


# --- before_call (incoming) over a real transport ---------------------------

async def test_server_before_call_fires_on_real_transport() -> None:
    seen: list[tuple[str, str, tuple]] = []
    server = IpcServer(
        TcpServerTransport("127.0.0.1", 0),
        {ICalculator: Calculator()},
        before_call=lambda ci: seen.append((ci.endpoint, ci.method_name, ci.arguments)),
    )
    async with server:
        host, port = _tcp_endpoint(server)
        async with IpcClient(TcpClientTransport(host, port)) as client:
            svc = client.get_proxy(ICalculator)
            assert await asyncio.wait_for(svc.Add(2.0, 3.0), timeout=5) == 5.0
    assert ("ICalculator", "Add", (2.0, 3.0)) in seen


# --- transport construction -----------------------------------------------

def test_tcp_server_transport_stores_host_and_port() -> None:
    t = TcpServerTransport("127.0.0.1", 0)
    assert t.host == "127.0.0.1"
    assert t.port == 0


def test_named_pipe_server_transport_addresses(monkeypatch) -> None:
    monkeypatch.delenv("TMPDIR", raising=False)
    t = NamedPipeServerTransport("calc")
    assert t._windows_address == r"\\.\pipe\calc"
    assert t._posix_address == os.path.join("/tmp", "CoreFxPipe_calc")


def test_server_transports_are_immutable() -> None:
    with pytest.raises(Exception):
        TcpServerTransport("127.0.0.1", 0).port = 1  # type: ignore[misc]
    with pytest.raises(Exception):
        NamedPipeServerTransport("calc").pipe_name = "x"  # type: ignore[misc]
