"""Reverse interop: a real .NET client against a Python `IpcServer`.

The mirror of test_dotnet_interop.py / IpcSample.PythonClientTestServer with
the roles swapped — Python hosts the services, .NET connects and calls them,
including handler-initiated reach-back into a .NET-hosted callback.

The Python `IpcServer` runs in-process on a named pipe; the .NET client
(`src/IpcSample.PythonServerTestClient`) is launched via `dotnet run` and
connects to it. Awaiting the subprocess keeps the event loop spinning so the
server accepts the connection and services requests concurrently. Requires
the `dotnet` CLI (skipped otherwise).
"""

from __future__ import annotations

import asyncio
import shutil
import sys
import uuid
from abc import ABC, abstractmethod
from pathlib import Path

import pytest

from uipath_ipc import IpcServer, Message, NamedPipeServerTransport

pytestmark = pytest.mark.integration

# This file lives at <repo>/src/Clients/python/uipath-ipc/tests/integration/ —
# six parents up to the repo root (same as the forward suite's conftest).
_REPO_ROOT = Path(__file__).resolve().parents[6]
_CLIENT_PROJECT = _REPO_ROOT / "src" / "IpcSample.PythonServerTestClient"
_RUN_TIMEOUT_SECONDS = 240.0  # first run builds the .NET client


# --- contracts + service the Python server hosts -------------------------

class IClientCallback(ABC):
    """Hosted by the .NET client; the server's GreetVia handler calls it."""

    @abstractmethod
    async def Decorate(self, name: str) -> str: ...


class IPythonService(ABC):
    """The endpoint contract. Its ``__name__`` keys the endpoint, and the
    server resolves incoming methods against it (only declared methods are
    reachable), so it must declare them — like the .NET interface. The impl's
    ``GreetVia`` adds a trailing ``Message`` not present here (reach-back), and
    the .NET client's trailing ``CancellationToken`` is never sent on the wire."""

    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...

    @abstractmethod
    async def EchoString(self, value: str) -> str: ...

    @abstractmethod
    async def MultiplyInts(self, x: int, y: int) -> int: ...

    @abstractmethod
    async def GreetVia(self, name: str) -> str: ...

    @abstractmethod
    async def FailWith(self, message: str) -> bool: ...


class PythonService:
    """Duck-typed service impl. Methods match the .NET IPythonService by name;
    the .NET client's trailing CancellationToken is never sent on the wire."""

    def __init__(self) -> None:
        self.calls: list[str] = []

    async def AddFloats(self, x: float, y: float) -> float:
        self.calls.append("AddFloats")
        return x + y

    async def EchoString(self, value: str) -> str:
        self.calls.append("EchoString")
        return value

    async def MultiplyInts(self, x: int, y: int) -> int:
        self.calls.append("MultiplyInts")
        return x * y

    async def GreetVia(self, name: str, m: Message) -> str:
        # Handler-initiated reach-back into the calling .NET client.
        self.calls.append("GreetVia")
        peer = m.client.get_callback(IClientCallback)
        decorated = await peer.Decorate(name)
        return f"hello {decorated}"

    async def FailWith(self, message: str) -> bool:
        raise ValueError(message)


def _skip_if_unavailable() -> None:
    if shutil.which("dotnet") is None:
        pytest.skip("dotnet CLI is not on PATH")
    if not _CLIENT_PROJECT.is_dir():
        pytest.fail(f"client project not found at {_CLIENT_PROJECT}")
    loop = asyncio.get_running_loop()
    if sys.platform == "win32" and not hasattr(loop, "start_serving_pipe"):
        pytest.skip("event loop is not a ProactorEventLoop; named pipes unsupported")


async def test_dotnet_client_calls_python_server() -> None:
    _skip_if_unavailable()
    pipe_name = f"uipath-ipc-pysrv-{uuid.uuid4().hex}"
    svc = PythonService()
    hooked: list[str] = []  # before_call (incoming) observed from a real .NET client
    server = IpcServer(
        NamedPipeServerTransport(pipe_name),
        {IPythonService: svc},
        before_call=lambda ci: hooked.append(ci.method_name),
    )

    async with server:
        proc = await asyncio.create_subprocess_exec(
            "dotnet", "run", "--", pipe_name,
            cwd=str(_CLIENT_PROJECT),
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.STDOUT,
        )
        try:
            stdout_bytes, _ = await asyncio.wait_for(
                proc.communicate(), timeout=_RUN_TIMEOUT_SECONDS
            )
        except asyncio.TimeoutError:
            proc.kill()
            await proc.wait()
            pytest.fail(f"dotnet client timed out after {_RUN_TIMEOUT_SECONDS}s")

    output = stdout_bytes.decode("utf-8", errors="replace")
    print("\n--- .NET client output ---\n" + output + "\n--------------------------")

    assert proc.returncode == 0, f"client exited {proc.returncode}:\n{output}"
    assert "ALL TESTS PASSED" in output
    # The in-process server observed every direct call plus the reach-back.
    assert {"AddFloats", "EchoString", "MultiplyInts", "GreetVia"} <= set(svc.calls)
    # The server's before_call (incoming) hook saw every .NET-initiated call —
    # including FailWith (the hook runs before the handler raises) — but NOT
    # Decorate, which is the server's own OUTGOING reach-back into the client.
    assert {"AddFloats", "EchoString", "MultiplyInts", "GreetVia", "FailWith"} <= set(hooked)
    assert "Decorate" not in hooked
