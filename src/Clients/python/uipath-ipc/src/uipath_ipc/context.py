"""Ambient, per-operation IPC context (`IpcContext.Current`) — the Python
counterpart of .NET's `IpcContext`, letting a POCO handler skip `Message`."""

from __future__ import annotations

import contextvars
from typing import TYPE_CHECKING, TypeVar

if TYPE_CHECKING:
    from .client.connection import IpcConnection

T = TypeVar("T")

#: Task-local by construction: an asyncio Task copies the current context at
#: creation, so a value set in one handler task never leaks to another.
_current: "contextvars.ContextVar[IpcContext | None]" = contextvars.ContextVar(
    "uipath_ipc_current_context", default=None
)


class _IpcContextMeta(type):
    @property
    def Current(cls) -> "IpcContext | None":  # noqa: N802 - PascalCase mirrors .NET/TS
        """The call being honored on the current asyncio task, or ``None``."""
        return _current.get()


class IpcContext(metaclass=_IpcContextMeta):
    """Ambient context of the IPC call being honored, letting a POCO handler reach
    the peer without a `Message` parameter. Coexists with `Message` injection."""

    __slots__ = ("_connection",)

    def __init__(self, connection: "IpcConnection") -> None:
        self._connection = connection

    def get_callback(self, contract: type[T]) -> T:
        """The ambient equivalent of ``message.client.get_callback(contract)``."""
        return self._connection.get_callback(contract)

    @staticmethod
    def _activate(connection: "IpcConnection") -> None:
        """Publish an `IpcContext` for `connection` as `Current` on this task. No
        reset needed — the caller's task owns its contextvars copy."""
        _current.set(IpcContext(connection))
