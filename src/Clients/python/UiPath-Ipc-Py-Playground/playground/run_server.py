"""Standalone server entry point."""

import asyncio
import sys
import os

# Add the library source to the path for development
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "UiPath-Ipc-Py", "src"))

from uipath_ipc import IpcServer, ContractCollection, TcpServerTransport
from playground.contracts import IComputingService
from playground.server_impl import ComputingService


async def main():
    endpoints = ContractCollection()
    endpoints.add(IComputingService, ComputingService())

    async with IpcServer(
        transport=TcpServerTransport("127.0.0.1", 5050),
        endpoints=endpoints,
    ) as server:
        print(f"Server listening on {server.transport}")
        print("Press Ctrl+C to stop.")
        try:
            await asyncio.Event().wait()
        except asyncio.CancelledError:
            pass


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\nServer stopped.")
