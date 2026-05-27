"""Standalone client entry point."""

import asyncio
import sys
import os

# Add the library source to the path for development
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "UiPath-Ipc-Py", "src"))

from uipath_ipc import IpcClient, TcpClientTransport
from playground.contracts import IComputingService


async def main():
    async with IpcClient(transport=TcpClientTransport("127.0.0.1", 5050)) as client:
        proxy = client.get_proxy(IComputingService)

        result = await proxy.AddFloats(1.23, 4.56)
        print(f"AddFloats(1.23, 4.56) = {result}")

        result = await proxy.MultiplyInts(6, 7)
        print(f"MultiplyInts(6, 7) = {result}")

        result = await proxy.Greet("Python")
        print(f"Greet('Python') = {result}")


if __name__ == "__main__":
    asyncio.run(main())
