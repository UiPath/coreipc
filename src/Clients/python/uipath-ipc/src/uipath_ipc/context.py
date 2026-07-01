"""Ambient, per-operation IPC context (`IpcContext.Current`).

The Python counterpart of the .NET ``IpcContext`` / ``AsyncLocal`` feature: it
lets a POCO service-contract handler reach the caller's callback WITHOUT a
`Message` parameter, so a contract module need not import anything from
``uipath_ipc``.
"""

from __future__ import annotations

import contextvars
from typing import TYPE_CHECKING, TypeVar

if TYPE_CHECKING:
    from .client.connection import IpcConnection

T = TypeVar("T")

#: Task-local by construction: an asyncio Task copies the current context at
#: creation, so a value set inside one handler task never leaks to the receive
#: loop or sibling handler tasks, and is dropped when that task ends.
_current: "contextvars.ContextVar[IpcContext | None]" = contextvars.ContextVar(
    "uipath_ipc_current_context", default=None
)


class _IpcContextMeta(type):
    @property
    def Current(cls) -> "IpcContext | None":  # noqa: N802 - PascalCase mirrors .NET/TS
        """The context of the IPC call being honored on the current asyncio
        task, or ``None`` if no call is in progress here."""
        return _current.get()


class IpcContext(metaclass=_IpcContextMeta):
    """Ambient, per-operation context for the IPC call currently being honored.

    ``IpcContext.Current`` is non-``None`` exactly while a server handler (or a
    callback) runs on this task, and ``None`` otherwise — so
    ``IpcContext.Current is not None`` is a precise "am I inside an IPC call?"
    signal. A POCO handler can reach the peer via
    ``IpcContext.Current.get_callback(SomeCallback)`` without declaring a
    `Message` parameter (the counterpart of .NET's ``IpcContext.Current``).
    Coexists with — does not replace — `Message` injection.
    """

    __slots__ = ("_connection",)

    def __init__(self, connection: "IpcConnection") -> None:
        self._connection = connection

    def get_callback(self, contract: type[T]) -> T:
        """Return a proxy to the peer's callback contract — the ambient
        equivalent of an injected ``message.client.get_callback(contract)``."""
        return self._connection.get_callback(contract)

    @staticmethod
    def _activate(connection: "IpcConnection") -> None:
        """Publish an `IpcContext` for `connection` as `Current` on this task.

        No explicit reset: the caller (`IpcConnection._invoke_callback`) runs in
        its own asyncio task, whose contextvars copy is task-local — the value
        never leaks to other tasks and is discarded when the task completes.
        """
        _current.set(IpcContext(connection))
