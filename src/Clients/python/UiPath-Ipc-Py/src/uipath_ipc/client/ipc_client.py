"""IpcClient: main client entry point, mirroring IpcClient.cs."""

from __future__ import annotations

from typing import Any, TypeVar

from ..transport.base import ClientTransport
from .service_client import ServiceClient

T = TypeVar("T")


class IpcClient:
    """IPC client that creates proxies for remote service contracts.

    Usage::

        client = IpcClient(transport=TcpClientTransport("127.0.0.1", 5050))
        proxy = client.get_proxy(IMyService)
        result = await proxy.MyMethod(arg1, arg2)
    """

    def __init__(
        self,
        transport: ClientTransport,
        request_timeout: float | None = None,
        debug_name: str | None = None,
    ) -> None:
        self._transport = transport
        self._request_timeout = request_timeout
        self._debug_name = debug_name
        self._service_clients: dict[type, ServiceClient] = {}

    @property
    def transport(self) -> ClientTransport:
        return self._transport

    def get_proxy(self, contract_type: type) -> Any:
        """Get a proxy for the given service contract type.

        The proxy intercepts method calls and routes them as RPC requests
        to the remote server. The connection is established lazily on
        the first method call.
        """
        if contract_type not in self._service_clients:
            self._service_clients[contract_type] = ServiceClient(
                transport=self._transport,
                interface_type=contract_type,
                request_timeout=self._request_timeout,
                debug_name=self._debug_name,
            )
        return self._service_clients[contract_type].proxy

    async def close(self) -> None:
        """Close all connections."""
        for sc in self._service_clients.values():
            await sc.close()
        self._service_clients.clear()

    async def __aenter__(self) -> IpcClient:
        return self

    async def __aexit__(self, *args: Any) -> None:
        await self.close()
