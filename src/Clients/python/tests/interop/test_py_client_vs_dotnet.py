"""Python client ↔ unmodified C# NodeInterop server.

These tests validate that our wire format + dispatch interoperates with the production
.NET implementation. Skipped automatically if the NodeInterop DLL isn't built.
"""

import asyncio

import pytest

from coreipc import IpcClient, operation, service
from coreipc.transport.named_pipe import NamedPipeClientTransport

from .dotnet_host import dotnet_server, locate_nodeinterop_dll

pytestmark = pytest.mark.skipif(
    locate_nodeinterop_dll() is None,
    reason="NodeInterop DLL not built — run `dotnet build src/Clients/js/dotnet/UiPath.CoreIpc.NodeInterop -f net6.0`",
)


# Mirrors the C# contract in
# src/Clients/js/dotnet/UiPath.CoreIpc.NodeInterop/Contracts.cs (IAlgebra).
@service
class IAlgebra:
    @operation
    async def Ping(self) -> str: ...

    @operation
    async def MultiplySimple(self, x: int, y: int) -> int: ...

    @operation
    async def Echo(self, x: int) -> int: ...


async def test_ping_returns_string(pipe_name):
    async with dotnet_server(pipe_name):
        client = IpcClient().with_transport(NamedPipeClientTransport(pipe_name))
        try:
            proxy = client.get_proxy(IAlgebra)
            result = await asyncio.wait_for(proxy.Ping(), timeout=10.0)
            assert isinstance(result, str) and len(result) > 0
        finally:
            await client.close()


async def test_multiply_simple(pipe_name):
    async with dotnet_server(pipe_name):
        client = IpcClient().with_transport(NamedPipeClientTransport(pipe_name))
        try:
            proxy = client.get_proxy(IAlgebra)
            assert await asyncio.wait_for(proxy.MultiplySimple(6, 7), timeout=10.0) == 42
        finally:
            await client.close()


async def test_echo(pipe_name):
    async with dotnet_server(pipe_name):
        client = IpcClient().with_transport(NamedPipeClientTransport(pipe_name))
        try:
            proxy = client.get_proxy(IAlgebra)
            assert await asyncio.wait_for(proxy.Echo(99), timeout=10.0) == 99
        finally:
            await client.close()


async def test_concurrent_calls_against_dotnet(pipe_name):
    async with dotnet_server(pipe_name):
        client = IpcClient().with_transport(NamedPipeClientTransport(pipe_name))
        try:
            proxy = client.get_proxy(IAlgebra)
            results = await asyncio.wait_for(
                asyncio.gather(
                    proxy.MultiplySimple(2, 3),
                    proxy.MultiplySimple(4, 5),
                    proxy.Echo(7),
                ),
                timeout=15.0,
            )
            assert results == [6, 20, 7]
        finally:
            await client.close()
