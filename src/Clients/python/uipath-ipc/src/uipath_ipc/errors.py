"""Public exception types for UiPath.Ipc."""

from __future__ import annotations

from .wire import Error


class RemoteException(Exception):
    """Raised by a proxy call when the server returned an `Error`.

    This is a thin placeholder until Phase C.1 refines exception
    propagation (chain, type-name mapping, etc.).
    """

    def __init__(self, error: Error) -> None:
        self.error = error
        super().__init__(error.message)


__all__ = ["RemoteException"]
