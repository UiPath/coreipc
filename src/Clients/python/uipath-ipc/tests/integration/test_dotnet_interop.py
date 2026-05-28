"""End-to-end tests against the real .NET IpcSample.ConsoleServer.

Skipped by default. Run with::

    pytest --integration

The .NET server is started once per pytest session by the
`dotnet_server` fixture (see conftest.py). It exposes IComputingService
and ISystemService on the named pipe ``test`` with a 2-second
request timeout.
"""

from __future__ import annotations

from abc import ABC, abstractmethod

import pytest

from uipath_ipc import IpcClient, NamedPipeClientTransport, RemoteException

from .conftest import DOTNET_PIPE_NAME

# Every test in this module needs the .NET server running.
pytestmark = pytest.mark.integration


# --- contracts (matching the .NET interfaces by name) --------------------

class IComputingService(ABC):
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...

    @abstractmethod
    async def AddComplexNumbers(self, a: dict, b: dict) -> dict: ...

    @abstractmethod
    async def MultiplyInts(self, x: int, y: int) -> int: ...

    @abstractmethod
    async def DivideByZero(self) -> bool: ...


class ISystemService(ABC):
    @abstractmethod
    async def EchoString(self, value: str) -> str: ...

    @abstractmethod
    async def ReverseBytes(self, bytes_: list[int]) -> list[int]: ...


# --- helpers --------------------------------------------------------------

def _new_client() -> IpcClient:
    return IpcClient(NamedPipeClientTransport(pipe_name=DOTNET_PIPE_NAME))


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
