"""E2E: `before_connect` as the server-lifecycle hook — the killer app.

The client OWNS its server: a `before_connect` hook lazily launches the real
.NET server process when there's nothing to connect to. And because the hook
runs before *every* (re)connect — not just the first — the pairing is
SELF-HEALING: if the server process dies, the very next call relaunches it
and succeeds, with no special handling at the call site.

The .NET server binary is launched directly (not via `dotnet run`, whose
wrapper process would survive a kill of the wrong member of the tree), so
`Process.kill()` genuinely makes the server disappear.
"""

from __future__ import annotations

import asyncio
import contextlib
import os
import shutil
import sys
import uuid
from abc import ABC, abstractmethod
from pathlib import Path

import pytest

from uipath_ipc import IpcClient, NamedPipeClientTransport

pytestmark = pytest.mark.integration

_REPO_ROOT = Path(__file__).resolve().parents[6]
_SERVER_PROJECT = _REPO_ROOT / "src" / "IpcSample.PythonClientTestServer"
_SERVER_EXE = (
    _SERVER_PROJECT
    / "bin"
    / "Debug"
    / "net8.0"
    / (
        "IpcSample.PythonClientTestServer.exe"
        if sys.platform == "win32"
        else "IpcSample.PythonClientTestServer"
    )
)

_READY_TIMEOUT_SECONDS = 60.0


class IComputingService(ABC):
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...

    @abstractmethod
    async def MultiplyInts(self, x: int, y: int) -> int: ...


async def _build_server() -> None:
    """`dotnet build` once so the apphost binary exists; no-op when current."""
    proc = await asyncio.create_subprocess_exec(
        "dotnet", "build", "-v", "quiet", "--nologo",
        cwd=str(_SERVER_PROJECT),
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.STDOUT,
    )
    out, _ = await asyncio.wait_for(proc.communicate(), timeout=240)
    assert proc.returncode == 0, f"dotnet build failed:\n{out.decode(errors='replace')}"
    assert _SERVER_EXE.exists(), f"expected server binary at {_SERVER_EXE}"


async def _drain(proc: asyncio.subprocess.Process) -> None:
    """Keep reading stdout so the server never blocks on a full pipe buffer."""
    assert proc.stdout is not None
    while await proc.stdout.readline():
        pass


async def test_before_connect_spawns_and_self_heals_dotnet_server() -> None:
    if shutil.which("dotnet") is None:
        pytest.skip("dotnet CLI is not on PATH")

    await _build_server()

    pipe_name = f"uipath-ipc-heal-{uuid.uuid4().hex}"
    procs: list[asyncio.subprocess.Process] = []
    drains: list[asyncio.Task[None]] = []
    launches = 0

    async def ensure_server() -> None:
        """The before_connect hook: (re)launch the server iff it isn't running."""
        nonlocal launches
        if procs and procs[-1].returncode is None:
            return  # server alive — nothing to do
        launches += 1
        if sys.platform != "win32":
            # A killed .NET server leaves its Unix socket file behind, which
            # would block the relaunch's bind.
            with contextlib.suppress(FileNotFoundError):
                os.unlink(f"/tmp/CoreFxPipe_{pipe_name}")
        proc = await asyncio.create_subprocess_exec(
            str(_SERVER_EXE), pipe_name,
            cwd=str(_SERVER_PROJECT),
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.STDOUT,
        )
        procs.append(proc)
        # Block until the pipe actually accepts connections (READY marker).
        assert proc.stdout is not None
        deadline = asyncio.get_running_loop().time() + _READY_TIMEOUT_SECONDS
        while True:
            remaining = deadline - asyncio.get_running_loop().time()
            line = await asyncio.wait_for(proc.stdout.readline(), timeout=max(remaining, 0.1))
            if not line:
                pytest.fail("server process exited before printing READY")
            if b"READY" in line:
                break
        drains.append(asyncio.create_task(_drain(proc)))

    client = IpcClient(
        NamedPipeClientTransport(pipe_name), before_connect=ensure_server
    )
    try:
        svc = client.get_proxy(IComputingService)

        # 1. First call: nothing is running — the hook launches the server.
        assert await asyncio.wait_for(svc.AddFloats(1.0, 2.0), timeout=30) == 3.0
        assert launches == 1

        # 2. Healthy connection: the hook does NOT refire.
        assert await asyncio.wait_for(svc.MultiplyInts(6, 7), timeout=30) == 42
        assert launches == 1

        # 3. The server DISAPPEARS (hard kill, simulating a crash).
        procs[0].kill()
        await procs[0].wait()

        # 4. The client notices the dead connection...
        deadline = asyncio.get_running_loop().time() + 10
        while not (client._connection is not None and client._connection.is_closed):
            if asyncio.get_running_loop().time() > deadline:
                pytest.fail("client did not observe the server's death")
            await asyncio.sleep(0.05)

        # 5. ...and the next ordinary call SELF-HEALS: before_connect
        #    relaunches the server and the call succeeds transparently.
        assert await asyncio.wait_for(svc.AddFloats(2.0, 3.0), timeout=30) == 5.0
        assert launches == 2
        assert procs[-1].returncode is None  # the relaunched server is alive
    finally:
        await client.aclose()
        for proc in procs:
            if proc.returncode is None:
                proc.kill()
                await proc.wait()
        for task in drains:
            task.cancel()
