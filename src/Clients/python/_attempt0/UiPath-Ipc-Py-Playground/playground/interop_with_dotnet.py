"""Interop test: starts the .NET IpcSample.ConsoleServer and connects from Python.

The .NET server exposes IComputingService and ISystemService over named pipes (pipe="test").
This script launches it via `dotnet run`, calls a few methods, then tears it down.

Requirements:
  - .NET SDK installed (dotnet CLI available)
  - pywin32 installed (pip install pywin32) for Windows named pipe support
"""

from __future__ import annotations

import asyncio
import os
import signal
import subprocess
import sys
import time
from abc import ABC, abstractmethod

# Add the library source to the path for development
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "UiPath-Ipc-Py", "src"))

from uipath_ipc import IpcClient, NamedPipeClientTransport

# Relative path from this script to the .NET ConsoleServer project
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_DOTNET_SERVER_PROJECT = os.path.normpath(
    os.path.join(_SCRIPT_DIR, "..", "..", "..", "..", "IpcSample.ConsoleServer")
)

PIPE_NAME = "test"


# -- Contracts matching the .NET interfaces --
# Method names are PascalCase to match the .NET wire format exactly.

class IComputingServiceBase(ABC):
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...


class IComputingService(IComputingServiceBase):
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


def start_dotnet_server() -> subprocess.Popen:
    """Start the .NET ConsoleServer via `dotnet run`."""
    print(f"Starting .NET server from: {_DOTNET_SERVER_PROJECT}")
    proc = subprocess.Popen(
        ["dotnet", "run", "--framework", "net6.0"],
        cwd=_DOTNET_SERVER_PROJECT,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if sys.platform == "win32" else 0,
    )
    # Wait for "Server started." in stdout
    print("Waiting for .NET server to start...")
    while True:
        line = proc.stdout.readline()
        if not line:
            raise RuntimeError("Server process exited unexpectedly.")
        print(f"  [.NET] {line.rstrip()}")
        if "Server started" in line:
            break
    print("Server is ready.\n")
    return proc


def stop_dotnet_server(proc: subprocess.Popen) -> None:
    """Stop the .NET server process."""
    print("\nStopping .NET server...")
    if sys.platform == "win32":
        # Send CTRL+C via CTRL_BREAK_EVENT on Windows
        proc.send_signal(signal.CTRL_BREAK_EVENT)
    else:
        proc.send_signal(signal.SIGINT)
    try:
        proc.wait(timeout=10)
    except subprocess.TimeoutExpired:
        proc.kill()
    print("Server stopped.")


async def run_interop_tests() -> None:
    """Connect to the .NET server and call methods."""
    transport = NamedPipeClientTransport(pipe_name=PIPE_NAME)

    async with IpcClient(transport=transport, request_timeout=5.0) as client:
        # -- IComputingService tests --
        print("=== IComputingService ===")

        computing = client.get_proxy(IComputingService)

        result = await computing.AddFloats(1.5, 2.5)
        print(f"  AddFloats(1.5, 2.5) = {result}")
        assert result == 4.0, f"Expected 4.0, got {result}"

        result = await computing.AddFloats(0.1, 0.2)
        print(f"  AddFloats(0.1, 0.2) = {result}")

        # AddComplexNumbers: .NET ComplexNumber has fields I and J
        a = {"I": 1.0, "J": 2.0}
        b = {"I": 3.0, "J": 4.0}
        result = await computing.AddComplexNumbers(a, b)
        print(f"  AddComplexNumbers({a}, {b}) = {result}")

        # -- ISystemService tests --
        print("\n=== ISystemService ===")

        system = client.get_proxy(ISystemService)

        result = await system.EchoString("Hello from Python!")
        print(f'  EchoString("Hello from Python!") = "{result}"')
        assert result == "Hello from Python!", f"Expected echo, got {result}"

        result = await system.EchoString("")
        print(f'  EchoString("") = "{result}"')

        # -- Error handling test --
        print("\n=== Error handling ===")
        try:
            await computing.DivideByZero()
            print("  DivideByZero() did NOT throw (unexpected)")
        except Exception as ex:
            print(f"  DivideByZero() correctly threw: {type(ex).__name__}: {ex.args[0]}")

    print("\nAll interop tests passed!")


def main() -> None:
    proc = start_dotnet_server()
    try:
        asyncio.run(run_interop_tests())
    finally:
        stop_dotnet_server(proc)


if __name__ == "__main__":
    main()
