"""Tests for the ambient IpcContext (POCO callback-capable contracts)."""

from __future__ import annotations

import asyncio
from abc import ABC, abstractmethod

from uipath_ipc import (
    IpcClient,
    IpcContext,
    IpcServer,
    TcpClientTransport,
    TcpServerTransport,
)

PONG = "pong-from-callback"


# A POCO contract: no `Message` parameter, yet callback-capable via `IpcContext.Current`.
class IContextProbe(ABC):
    @abstractmethod
    async def ReachCallbackViaContext(self) -> str: ...

    @abstractmethod
    async def ContextIsSet(self) -> bool: ...


class IContextProbeCallback(ABC):
    @abstractmethod
    async def Pong(self) -> str: ...


class ContextProbe:  # duck-typed; no Message anywhere in sight
    async def ReachCallbackViaContext(self) -> str:
        ctx = IpcContext.Current
        assert ctx is not None
        return await ctx.get_callback(IContextProbeCallback).Pong()

    async def ContextIsSet(self) -> bool:
        return IpcContext.Current is not None


class ContextProbeCallback:
    async def Pong(self) -> str:
        return PONG


def _endpoint(server: IpcServer) -> tuple[str, int]:
    assert server.handle is not None
    return server.handle.sockets[0].getsockname()[:2]  # type: ignore[attr-defined]


def test_current_is_none_outside_a_call() -> None:
    assert IpcContext.Current is None


async def test_current_is_set_while_honoring_a_call() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {IContextProbe: ContextProbe()})
    async with server:
        host, port = _endpoint(server)
        async with IpcClient(
            TcpClientTransport(host, port),
            callbacks={IContextProbeCallback: ContextProbeCallback()},
        ) as client:
            svc = client.get_proxy(IContextProbe)
            assert await asyncio.wait_for(svc.ContextIsSet(), timeout=5) is True
    # The ambient value must not have leaked into the test's own task.
    assert IpcContext.Current is None


async def test_poco_contract_reaches_callback_via_ipccontext() -> None:
    server = IpcServer(TcpServerTransport("127.0.0.1", 0), {IContextProbe: ContextProbe()})
    async with server:
        host, port = _endpoint(server)
        async with IpcClient(
            TcpClientTransport(host, port),
            callbacks={IContextProbeCallback: ContextProbeCallback()},
        ) as client:
            svc = client.get_proxy(IContextProbe)
            assert await asyncio.wait_for(svc.ReachCallbackViaContext(), timeout=5) == PONG
