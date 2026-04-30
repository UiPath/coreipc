import asyncio
import time

import pytest

from coreipc import CancellationTokenSource, IpcClient, IpcServer, RemoteError
from coreipc.transport.named_pipe import NamedPipeClientTransport, NamedPipeServerTransport

from .contracts import IAlgebra, IArithmetic


class AlgebraImpl:
    async def MultiplySimple(self, x, y):
        return x * y

    async def Sleep(self, milliseconds, ct=None):
        if ct is None:
            await asyncio.sleep(milliseconds / 1000.0)
            return True
        done = asyncio.create_task(ct.wait_cancelled())
        timer = asyncio.create_task(asyncio.sleep(milliseconds / 1000.0))
        _, pending = await asyncio.wait(
            [done, timer], return_when=asyncio.FIRST_COMPLETED
        )
        for t in pending:
            t.cancel()
        if ct.is_cancelled:
            raise asyncio.CancelledError()
        return True

    async def DivideByZero(self, x):
        return x / 0

    async def CallBackSum(self, x, y, msg=None):
        cb = msg.client.get_callback(IArithmetic)
        return await cb.Sum(x, y)


class ArithmeticImpl:
    async def Sum(self, x, y):
        return x + y


@pytest.fixture
async def server_and_client(pipe_name):
    server = (
        IpcServer()
        .with_transport(NamedPipeServerTransport(pipe_name))
        .with_service(IAlgebra, AlgebraImpl())
    )
    await server.start()
    client = (
        IpcClient()
        .with_transport(NamedPipeClientTransport(pipe_name))
        .with_callback(IArithmetic, ArithmeticImpl())
    )
    try:
        yield server, client
    finally:
        await client.close()
        await server.stop()


async def test_simple_round_trip(server_and_client):
    _, client = server_and_client
    proxy = client.get_proxy(IAlgebra)
    assert await proxy.MultiplySimple(2, 3) == 6


async def test_concurrent_calls_on_single_connection(server_and_client):
    _, client = server_and_client
    proxy = client.get_proxy(IAlgebra)
    slow = asyncio.create_task(proxy.Sleep(500))
    # Give the slow call a head start so we know it's in-flight.
    await asyncio.sleep(0.01)
    fast_start = time.monotonic()
    assert await proxy.Sleep(1) is True
    fast_elapsed = time.monotonic() - fast_start
    assert not slow.done(), "slow call completed too early — calls aren't multiplexed"
    assert fast_elapsed < 0.3
    assert await slow is True


async def test_cancellation_propagates_to_server(server_and_client):
    _, client = server_and_client
    proxy = client.get_proxy(IAlgebra)
    cts = CancellationTokenSource()
    asyncio.get_running_loop().call_later(0.05, cts.cancel)
    t0 = time.monotonic()
    with pytest.raises(asyncio.CancelledError):
        await proxy.Sleep(5000, cts.token)
    assert time.monotonic() - t0 < 1.0


async def test_remote_exception_is_raised(server_and_client):
    _, client = server_and_client
    proxy = client.get_proxy(IAlgebra)
    with pytest.raises(RemoteError) as excinfo:
        await proxy.DivideByZero(1)
    # The real remote type is Python's; the field carries the fully-qualified name.
    assert "ZeroDivisionError" in excinfo.value.remote_type


async def test_bidirectional_callback(server_and_client):
    _, client = server_and_client
    proxy = client.get_proxy(IAlgebra)
    # Server's CallBackSum calls back to the client's IArithmetic.Sum(3, 4) -> 7.
    assert await proxy.CallBackSum(3, 4) == 7
