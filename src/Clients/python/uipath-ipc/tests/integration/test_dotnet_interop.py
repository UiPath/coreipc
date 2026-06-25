"""End-to-end tests against the real .NET IpcSample.ConsoleServer.

These run as part of the default ``pytest`` invocation. Pass
``--no-integration`` to skip them (e.g. for fast unit-only loops).

The .NET server is started once per pytest session by the
`dotnet_server` fixture (see conftest.py). It exposes IComputingService
and ISystemService on the named pipe ``test`` with a 2-second
request timeout.
"""

from __future__ import annotations

import asyncio
import datetime as dt
from abc import ABC, abstractmethod
from uuid import UUID

import pytest

from uipath_ipc import (
    INFINITE_REQUEST_TIMEOUT,
    IpcClient,
    Message,
    NamedPipeClientTransport,
    RemoteException,
    ipc_cancellable,
)

from .conftest import DOTNET_PIPE_NAME

# Every test in this module needs the .NET server running.
pytestmark = pytest.mark.integration


# --- contracts (matching the .NET interfaces by name) --------------------

# Every .NET method here ends with `CancellationToken ct = default`, so the
# Python signatures omit it and carry @ipc_cancellable to say so — exercising
# the marker end-to-end against the real server (it must stay a wire no-op).

class IComputingService(ABC):
    @ipc_cancellable
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...

    @ipc_cancellable
    @abstractmethod
    async def AddComplexNumbers(self, a: dict, b: dict) -> dict: ...

    @ipc_cancellable
    @abstractmethod
    async def MultiplyInts(self, x: int, y: int) -> int: ...

    @ipc_cancellable
    @abstractmethod
    async def DivideByZero(self) -> bool: ...

    @ipc_cancellable
    @abstractmethod
    async def Wait(self, duration: str) -> bool: ...

    @ipc_cancellable
    @abstractmethod
    async def WaitWithMessage(self, duration: str, m: object) -> bool: ...


class ISystemService(ABC):
    @ipc_cancellable
    @abstractmethod
    async def EchoString(self, value: str) -> str: ...

    @ipc_cancellable
    @abstractmethod
    async def ReverseBytes(self, data: bytes) -> bytes: ...

    @ipc_cancellable
    @abstractmethod
    async def EchoGuid(self, value: UUID) -> UUID: ...

    @ipc_cancellable
    @abstractmethod
    async def EchoDateTime(self, value: dt.datetime) -> dt.datetime: ...


# Callback contracts — IClientCallback is the contract the *client* hosts;
# ICallbackTester is the server endpoint that invokes IClientCallback back.

# IClientCallback is deliberately NOT @ipc_cancellable: the .NET callback
# interfaces declare no CancellationToken (so the server never sends a ct slot
# when calling back) — verifying the unannotated path still works alongside the
# annotated one.
class IClientCallback(ABC):
    @abstractmethod
    async def EchoToClient(self, value: str) -> str: ...

    @abstractmethod
    async def AddOnClient(self, x: int, y: int) -> int: ...


class ICallbackTester(ABC):
    @ipc_cancellable
    @abstractmethod
    async def TriggerEcho(self, value: str) -> str: ...

    @ipc_cancellable
    @abstractmethod
    async def TriggerAdd(self, x: int, y: int) -> int: ...


# --- helpers --------------------------------------------------------------

def _new_client() -> IpcClient:
    return IpcClient(NamedPipeClientTransport(pipe_name=DOTNET_PIPE_NAME))


def _new_client_with_callback(callback: object) -> IpcClient:
    return IpcClient(
        NamedPipeClientTransport(pipe_name=DOTNET_PIPE_NAME),
        callbacks={IClientCallback: callback},
    )


# --- tests ----------------------------------------------------------------

async def test_add_floats(dotnet_server) -> None:
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        assert await svc.AddFloats(1.5, 2.5) == 4.0


async def test_multiply_ints(dotnet_server) -> None:
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        assert await svc.MultiplyInts(6, 7) == 42


async def test_echo_string(dotnet_server) -> None:
    async with _new_client() as client:
        svc = client.get_proxy(ISystemService)
        assert await svc.EchoString("Hello from Python!") == "Hello from Python!"


async def test_echo_empty_string(dotnet_server) -> None:
    async with _new_client() as client:
        svc = client.get_proxy(ISystemService)
        assert await svc.EchoString("") == ""


async def test_add_complex_numbers(dotnet_server) -> None:
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        a = {"I": 1.0, "J": 2.0}
        b = {"I": 3.0, "J": 4.0}
        result = await svc.AddComplexNumbers(a, b)
        assert result["I"] == 4.0
        assert result["J"] == 6.0


async def test_divide_by_zero_raises_remote_exception(dotnet_server) -> None:
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        with pytest.raises(RemoteException) as ex_info:
            await svc.DivideByZero()
        # The .NET side throws DivideByZeroException; type_name should reflect it.
        assert "DivideByZero" in (ex_info.value.type_name or "")


async def test_multiple_calls_reuse_connection(dotnet_server) -> None:
    """Sanity check that the same client handles a sequence of calls."""
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        assert await svc.AddFloats(1.0, 2.0) == 3.0
        assert await svc.AddFloats(3.0, 4.0) == 7.0
        assert await svc.MultiplyInts(5, 6) == 30


# --- server-to-client callbacks ------------------------------------------

class _EchoCallback:
    """Simple IClientCallback implementation for the callback tests."""

    def __init__(self) -> None:
        self.echo_calls: list[str] = []
        self.add_calls: list[tuple[int, int]] = []

    async def EchoToClient(self, value: str) -> str:
        self.echo_calls.append(value)
        return f"echoed: {value}"

    async def AddOnClient(self, x: int, y: int) -> int:
        self.add_calls.append((x, y))
        return x + y


async def test_server_invokes_client_callback_echo(dotnet_server) -> None:
    cb = _EchoCallback()
    async with _new_client_with_callback(cb) as client:
        tester = client.get_proxy(ICallbackTester)
        result = await tester.TriggerEcho("hi from server")
        assert result == "echoed: hi from server"
        assert cb.echo_calls == ["hi from server"]


async def test_server_invokes_client_callback_with_multiple_args(dotnet_server) -> None:
    cb = _EchoCallback()
    async with _new_client_with_callback(cb) as client:
        tester = client.get_proxy(ICallbackTester)
        assert await tester.TriggerAdd(7, 8) == 15
        assert cb.add_calls == [(7, 8)]


async def test_multiple_server_initiated_callbacks_on_same_client(dotnet_server) -> None:
    """Verify a single client handles a series of inbound callbacks."""
    cb = _EchoCallback()
    async with _new_client_with_callback(cb) as client:
        tester = client.get_proxy(ICallbackTester)
        results = [
            await tester.TriggerEcho("a"),
            await tester.TriggerEcho("b"),
            await tester.TriggerEcho("c"),
        ]
        assert results == ["echoed: a", "echoed: b", "echoed: c"]
        assert cb.echo_calls == ["a", "b", "c"]


# --- value-type round-trips (type-directed (de)serialization) --------------
# Each fails without the serialization layer: .NET sends byte[] as base64 and
# Guid/DateTime as strings, which a bare json.loads leaves as a str.

async def test_reverse_bytes_round_trips_as_bytes(dotnet_server) -> None:
    async with _new_client() as client:
        svc = client.get_proxy(ISystemService)
        assert await svc.ReverseBytes(b"\x01\x02\x03\x04") == b"\x04\x03\x02\x01"


async def test_guid_round_trips_as_uuid(dotnet_server) -> None:
    u = UUID("550e8400-e29b-41d4-a716-446655440000")
    async with _new_client() as client:
        svc = client.get_proxy(ISystemService)
        result = await svc.EchoGuid(u)
        assert result == u and isinstance(result, UUID)


async def test_datetime_round_trips_as_datetime(dotnet_server) -> None:
    d = dt.datetime(2026, 6, 12, 10, 30, 0, tzinfo=dt.timezone.utc)
    async with _new_client() as client:
        svc = client.get_proxy(ISystemService)
        result = await svc.EchoDateTime(d)
        assert isinstance(result, dt.datetime)
        assert result == d


async def test_datetime_with_microseconds_round_trips(dotnet_server) -> None:
    """Non-zero microseconds survive end-to-end through the real .NET server —
    exercises the 7->6 fractional-digit handling against Newtonsoft, not just in
    the unit test."""
    d = dt.datetime(2026, 6, 12, 10, 30, 0, 123456, tzinfo=dt.timezone.utc)
    async with _new_client() as client:
        svc = client.get_proxy(ISystemService)
        result = await svc.EchoDateTime(d)
        assert isinstance(result, dt.datetime)
        assert result == d


# --- per-call timeout (Message argument) -----------------------------------
# The .NET server's default RequestTimeout is 2 seconds (see conftest /
# Program.cs). These three tests triangulate the per-call feature end to end:
# the control proves the 2s default really applies, the override proves a
# Message-borne timeout beats it on the wire, and the deadline test proves
# the same Message timeout also enforces a client-side cutoff.

async def test_server_default_timeout_applies_without_message(dotnet_server) -> None:
    """Control: a 3s operation with NO per-call timeout dies at the server's
    2s default — proving the override test below succeeds *because of* the
    Message-borne timeout, not by accident."""
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        with pytest.raises(RemoteException):
            await svc.Wait("00:00:03")


async def test_per_call_timeout_overrides_server_default(dotnet_server) -> None:
    """A Message(request_timeout=10) rides the Request envelope as
    TimeoutInSeconds and overrides the server's 2s default: the same 3s
    operation that the control test saw cancelled now completes."""
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        assert await svc.WaitWithMessage("00:00:03", Message(request_timeout=10.0)) is True


async def test_per_call_timeout_bounds_the_call_promptly(dotnet_server) -> None:
    """A Message(request_timeout=0.5) bounds a 10s operation promptly. The same
    0.5s rides the wire AND the client's local deadline (.NET parity — one value
    sets both), so it's a race which fires first: the client's local wait_for
    (asyncio.TimeoutError) or the server enforcing the wire timeout
    (System.TimeoutException). Either proves the call was bounded — assert that,
    not which one won (the previous strict asyncio.TimeoutError check flaked
    under load when the server's response beat the local deadline)."""
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        start = asyncio.get_running_loop().time()
        with pytest.raises((asyncio.TimeoutError, RemoteException)) as ei:
            await svc.WaitWithMessage("00:00:10", Message(request_timeout=0.5))
        elapsed = asyncio.get_running_loop().time() - start
        assert elapsed < 2.0, f"timeout did not bound the call ({elapsed:.2f}s)"
        if isinstance(ei.value, RemoteException):
            assert ei.value.type_name == "System.TimeoutException"


async def test_infinite_per_call_timeout_overrides_server_default(dotnet_server) -> None:
    """INFINITE_REQUEST_TIMEOUT (-0.001, .NET Timeout.InfiniteTimeSpan — what
    the TS client sends for sign-in/disconnect) disables the server's 2s
    default outright: the 3s operation completes."""
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        assert await svc.WaitWithMessage(
            "00:00:03", Message(request_timeout=INFINITE_REQUEST_TIMEOUT)
        ) is True


# --- cancellation through an @ipc_cancellable method ------------------------

async def test_cancellation_propagates_through_cancellable_method(dotnet_server) -> None:
    """Cancelling an in-flight call to an @ipc_cancellable .NET method (whose
    handler awaits Task.Delay(duration, ct)) unblocks promptly: the asyncio
    cancellation is delivered to the server out-of-band as a CancellationRequest
    frame, which fires the .NET CancellationToken. Proves the marker is just
    documentation — cancellation never rides as a parameter."""
    async with _new_client() as client:
        svc = client.get_proxy(IComputingService)
        task = asyncio.create_task(svc.Wait("00:00:10"))
        # Let the request reach the server and start its Task.Delay.
        await asyncio.sleep(0.3)
        assert not task.done()

        task.cancel()
        start = asyncio.get_running_loop().time()
        with pytest.raises(asyncio.CancelledError):
            await task
        assert asyncio.get_running_loop().time() - start < 2.0, "cancel was not prompt"

        # Let the fire-and-forget CancellationRequest flush to the server before
        # the context manager closes the connection.
        await asyncio.sleep(0.2)


# --- before_call hook (outgoing only — .NET parity) -------------------------

async def test_before_call_fires_for_outgoing_calls_not_for_callbacks(dotnet_server) -> None:
    """Mirrors .NET's BeforeCall_ShouldApplyToCallsButNotToCallbacks: the
    client's before_call sees its own outgoing TriggerEcho, but NOT the
    inbound EchoToClient callback the server makes during that same call."""
    seen: list[tuple[str, str]] = []
    cb = _EchoCallback()
    client = IpcClient(
        NamedPipeClientTransport(pipe_name=DOTNET_PIPE_NAME),
        callbacks={IClientCallback: cb},
        before_call=lambda ci: seen.append((ci.endpoint, ci.method_name)),
    )
    async with client:
        tester = client.get_proxy(ICallbackTester)
        assert await tester.TriggerEcho("hooked") == "echoed: hooked"
        assert cb.echo_calls == ["hooked"]  # the callback really ran...
    assert ("ICallbackTester", "TriggerEcho") in seen
    assert not any(m == "EchoToClient" for _, m in seen)  # ...but unhooked
