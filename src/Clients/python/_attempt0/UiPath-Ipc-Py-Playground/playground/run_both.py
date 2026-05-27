"""Run server and client in the same process for quick testing."""

import asyncio
import sys
import os

# Add the library source to the path for development
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "UiPath-Ipc-Py", "src"))

from uipath_ipc import (
    IpcServer,
    IpcClient,
    ContractCollection,
    TcpServerTransport,
    TcpClientTransport,
)
from playground.contracts import IComputingService
from playground.server_impl import ComputingService


async def main():
    # Set up server
    endpoints = ContractCollection()
    endpoints.add(IComputingService, ComputingService())

    # Use port 0 for auto-assignment to avoid conflicts
    server_transport = TcpServerTransport("127.0.0.1", 0)

    async with IpcServer(
        transport=server_transport,
        endpoints=endpoints,
    ) as server:
        port = server_transport.port
        print(f"Server started on port {port}")

        # Set up client
        async with IpcClient(transport=TcpClientTransport("127.0.0.1", port)) as client:
            proxy = client.get_proxy(IComputingService)

            # Make some calls
            result = await proxy.AddFloats(1.23, 4.56)
            print(f"AddFloats(1.23, 4.56) = {result}")

            result = await proxy.MultiplyInts(6, 7)
            print(f"MultiplyInts(6, 7) = {result}")

            result = await proxy.Greet("Python IPC")
            print(f"Greet('Python IPC') = {result}")

            print("\nAll calls succeeded!")


if __name__ == "__main__":
    asyncio.run(main())
