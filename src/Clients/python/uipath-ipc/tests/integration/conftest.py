"""Fixtures for tests that talk to the dedicated .NET test server.

The server lives at `src/IpcSample.PythonClientTestServer/` and is
purpose-built for this suite:
  - console logging is wired up,
  - the startup marker (``READY pipe=...``) is printed after
    `WaitForStart()` so the pipe is *actually* accepting connections,
  - no callback dependencies, so every method works against a
    callback-less Python client.

The server is launched once per pytest session via `dotnet run`. A
background thread continuously reads its stdout so the full transcript
is dumped at session teardown for diagnostics.
"""

from __future__ import annotations

import shutil
import signal
import subprocess
import sys
import threading
from pathlib import Path
from typing import Iterator

import pytest

# This file lives at:
#   <repo>/src/Clients/python/uipath-ipc/tests/integration/conftest.py
# That's 6 parents up to the repo root.
_REPO_ROOT = Path(__file__).resolve().parents[6]
_SERVER_PROJECT = _REPO_ROOT / "src" / "IpcSample.PythonClientTestServer"

DOTNET_PIPE_NAME = "uipath-ipc-py-test"

_STARTUP_TIMEOUT_SECONDS = 60.0
_READY_MARKER = f"READY pipe={DOTNET_PIPE_NAME}"


@pytest.fixture(scope="session")
def dotnet_server() -> Iterator[subprocess.Popen]:
    """Spin up the dedicated .NET test server for the duration of the session."""
    if shutil.which("dotnet") is None:
        pytest.skip("dotnet CLI is not on PATH")
    if not _SERVER_PROJECT.is_dir():
        pytest.fail(f"test server project not found at {_SERVER_PROJECT}")

    creationflags = (
        subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == "win32" else 0
    )

    proc = subprocess.Popen(
        ["dotnet", "run", "--", DOTNET_PIPE_NAME],
        cwd=str(_SERVER_PROJECT),
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,  # line-buffered
        creationflags=creationflags,
    )

    assert proc.stdout is not None

    server_lines: list[str] = []
    ready = threading.Event()

    def _drain() -> None:
        """Continuously read stdout into the buffer; signal when READY appears."""
        assert proc.stdout is not None
        for line in proc.stdout:
            server_lines.append(line)
            if not ready.is_set() and _READY_MARKER in line:
                ready.set()

    reader = threading.Thread(target=_drain, daemon=True)
    reader.start()

    if not ready.wait(timeout=_STARTUP_TIMEOUT_SECONDS):
        proc.kill()
        raise RuntimeError(
            f"server did not signal {_READY_MARKER!r} within "
            f"{_STARTUP_TIMEOUT_SECONDS}s; captured output:\n"
            + "".join(server_lines)
        )

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

        reader.join(timeout=2)

        # Dump everything the server printed — visible in pytest's
        # "Captured stdout" for the session if anything went wrong.
        if server_lines:
            print("\n--- .NET server output --------------------------------------")
            print("".join(server_lines))
            print("-------------------------------------------------------------")
