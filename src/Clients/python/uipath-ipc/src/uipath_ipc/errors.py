"""Public exception types for UiPath.Ipc."""

from __future__ import annotations

from .wire import Error


class RemoteException(Exception):
    """Raised by a proxy call when the server returned an `Error` response.

    Carries the original .NET exception's metadata:

    Attributes:
        message: The error message text.
        type_name: The fully-qualified .NET type name (e.g.
            ``"System.InvalidOperationException"``), or None if unset.
        stack_trace: The server-side stack trace as a string, or None.
        inner: The inner `RemoteException`, mirroring the nested `Error`
            chain. Python's `__cause__` is also set so tracebacks display
            the chain naturally.
    """

    def __init__(
        self,
        message: str,
        type_name: str | None = None,
        stack_trace: str | None = None,
        inner: RemoteException | None = None,
    ) -> None:
        super().__init__(message)
        self.message = message
        self.type_name = type_name
        self.stack_trace = stack_trace
        self.inner = inner

    @classmethod
    def from_error(cls, error: Error) -> RemoteException:
        """Build a `RemoteException` (and its chain) from a wire `Error`."""
        inner = cls.from_error(error.inner_error) if error.inner_error else None
        exc = cls(
            message=error.message,
            type_name=error.type_name,
            stack_trace=error.stack_trace,
            inner=inner,
        )
        if inner is not None:
            exc.__cause__ = inner  # so tracebacks display the chain
        return exc

    def __str__(self) -> str:  # noqa: D401
        if self.type_name:
            return f"[{self.type_name}] {self.message}"
        return self.message


__all__ = ["RemoteException"]
