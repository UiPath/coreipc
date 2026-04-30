from __future__ import annotations

import asyncio
import json
import os
from contextlib import asynccontextmanager
from pathlib import Path

READY_SIGNAL_PREFIX = "###"
READY_KIND = "ReadyToConnect"
POWERING_ON_KIND = "PoweringOn"


def locate_nodeinterop_dll() -> Path | None:
    """Find the NodeInterop test-server DLL produced by building that .NET project.

    Returns None if the binary isn't built; callers should skip interop tests in that
    case. Env var IPC_DOTNET_TEST_SERVER wins if set, so CI can point elsewhere.
    """
    override = os.environ.get("IPC_DOTNET_TEST_SERVER")
    if override:
        p = Path(override)
        return p if p.is_file() else None
    # This file lives at <repo>/src/Clients/python/tests/interop/dotnet_host.py — repo
    # root is parents[5]. The DLL lives under src/Clients/js/dotnet/... (same tree the
    # TS harness points at).
    repo_root = Path(__file__).resolve().parents[5]
    candidate = (
        repo_root
        / "src"
        / "Clients"
        / "js"
        / "dotnet"
        / "UiPath.CoreIpc.NodeInterop"
        / "bin"
        / "Debug"
        / "net6.0"
        / "UiPath.CoreIpc.NodeInterop.dll"
    )
    return candidate if candidate.is_file() else None


@asynccontextmanager
async def dotnet_server(pipe_name: str, *, ready_timeout: float = 30.0):
    """Spawn the .NET NodeInterop test server for the duration of a `with` block.

    Blocks until the ReadyToConnect signal is observed on stdout, then yields the
    subprocess handle. On exit, terminates the process.
    """
    dll = locate_nodeinterop_dll()
    if dll is None:
        raise RuntimeError(
            "NodeInterop DLL not found. Build `src/Clients/js/dotnet/UiPath.CoreIpc.NodeInterop` "
            "for net6.0 or set IPC_DOTNET_TEST_SERVER."
        )

    proc = await asyncio.create_subprocess_exec(
        "dotnet",
        str(dll),
        "--pipe",
        pipe_name,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        stdin=asyncio.subprocess.PIPE,
    )

    ready = asyncio.Event()
    stdout_lines: list[str] = []
    stderr_lines: list[str] = []
    ready_error: dict | None = None

    async def drain_stdout() -> None:
        nonlocal ready_error
        while True:
            line = await proc.stdout.readline()
            if not line:
                return
            text = line.decode(errors="replace").rstrip("\r\n")
            stdout_lines.append(text)
            if text.startswith(READY_SIGNAL_PREFIX):
                try:
                    signal = json.loads(text[len(READY_SIGNAL_PREFIX):])
                except json.JSONDecodeError:
                    continue
                kind = signal.get("Kind")
                if kind == READY_KIND:
                    details = signal.get("Details")
                    if details is not None:
                        ready_error = details
                    ready.set()

    async def drain_stderr() -> None:
        while True:
            line = await proc.stderr.readline()
            if not line:
                return
            stderr_lines.append(line.decode(errors="replace").rstrip("\r\n"))

    stdout_task = asyncio.create_task(drain_stdout())
    stderr_task = asyncio.create_task(drain_stderr())

    try:
        try:
            await asyncio.wait_for(ready.wait(), timeout=ready_timeout)
        except asyncio.TimeoutError as ex:
            raise RuntimeError(
                "Timed out waiting for .NET ReadyToConnect.\n"
                f"stdout:\n  {os.linesep.join(stdout_lines)}\n"
                f"stderr:\n  {os.linesep.join(stderr_lines)}"
            ) from ex

        if ready_error is not None:
            raise RuntimeError(f".NET server reported startup error: {ready_error}")

        yield proc

    finally:
        if proc.returncode is None:
            proc.terminate()
            try:
                await asyncio.wait_for(proc.wait(), timeout=5.0)
            except asyncio.TimeoutError:
                proc.kill()
                await proc.wait()
        stdout_task.cancel()
        stderr_task.cancel()
        for t in (stdout_task, stderr_task):
            try:
                await t
            except (asyncio.CancelledError, Exception):
                pass
