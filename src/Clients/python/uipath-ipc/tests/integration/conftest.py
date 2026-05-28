"""Fixtures for tests that talk to the real .NET IpcSample.ConsoleServer.

The server is launched once per pytest session via `dotnet run`. It
listens on the named pipe ``test``. Tests get a no-op fixture value;
the side effect (server running) is what they consume.
"""

from __future__ import annotations

import shutil
import signal
import subprocess
import sys
from pathlib import Path
from typing import Iterator

import pytest

# This file lives at:
#   <repo>/src/Clients/python/uipath-ipc/tests/integration/conftest.py
# That's 6 parents up to the repo root.
_REPO_ROOT = Path(__file__).resolve().parents[6]
_SERVER_PROJECT = _REPO_ROOT / "src" / "IpcSample.ConsoleServer"

DOTNET_PIPE_NAME = "test"


@pytest.fixture(scope="session")
def dotnet_server() -> Iterator[subprocess.Popen]:
    """Spin up `IpcSample.ConsoleServer` for the duration of the test session."""
    if shutil.which("dotnet") is None:
        pytest.skip("dotnet CLI is not on PATH")
    if not _SERVER_PROJECT.is_dir():
        pytest.fail(f"sample server project not found at {_SERVER_PROJECT}")

    creationflags = (
        subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == "win32" else 0
    )

    proc = subprocess.Popen(
        ["dotnet", "run", "--framework", "net6.0"],
        cwd=str(_SERVER_PROJECT),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        creationflags=creationflags,
    )

    assert proc.stdout is not None

    # Wait for the server's startup line. If the process exits before
    # printing it, surface the captured output.
    captured: list[str] = []
    try:
        while True:
            line = proc.stdout.readline()
            if not line:
                proc.wait(timeout=5)
                raise RuntimeError(
                    "server exited before signalling startup:\n"
                    + "".join(captured)
                )
            captured.append(line)
            if "Server started" in line:
                break
    except BaseException:
        proc.kill()
        raise

    try:
        yield proc
    finally:
        if sys.platform == "win32":
            proc.send_signal(signal.CTRL_BREAK_EVENT)
        else:
            proc.send_signal(signal.SIGINT)
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
