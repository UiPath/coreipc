from __future__ import annotations

from typing import Any

from .proxy import build_proxy


class CallbackClient:
    """Proxy factory for reverse-invocations over an existing Connection.

    Injected into a service method's Message parameter on the server side so the method
    can invoke callbacks on the calling peer — e.g. `msg.client.get_callback(IArithmetic).Sum(...)`.
    """

    def __init__(self, connection: Any) -> None:
        self._connection = connection

    def get_callback(self, contract_cls: type) -> Any:
        return build_proxy(contract_cls, self._connection)
