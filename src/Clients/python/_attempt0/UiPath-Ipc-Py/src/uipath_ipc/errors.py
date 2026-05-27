"""Exception types for IPC errors."""

from __future__ import annotations

from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from .wire.dtos import Error


class RemoteException(Exception):
    """Wraps an error received from a remote IPC endpoint."""

    STACK_TRACE_SEPARATOR = "--- End of stack trace from previous location ---"

    def __init__(self, error: Error) -> None:
        super().__init__(error.Message)
        self.error_type: str = error.Type
        self.remote_stack_trace: str | None = error.StackTrace
        self.inner_exception: RemoteException | None = (
            RemoteException(error.InnerError) if error.InnerError else None
        )

    def is_type(self, type_name: str) -> bool:
        """Check if the remote exception was of a given type (by full name)."""
        return self.error_type == type_name

    def __str__(self) -> str:
        parts: list[str] = []
        self._gather(parts)
        return "".join(parts)

    def _gather(self, parts: list[str]) -> None:
        parts.append(f"RemoteException wrapping {self.error_type}: {self.args[0]} ")
        if self.inner_exception is None:
            parts.append("\n")
        else:
            parts.append(" ---> ")
            self.inner_exception._gather(parts)
            parts.append("\n\t--- End of inner exception stack trace ---\n")
        if self.remote_stack_trace:
            parts.append(self.remote_stack_trace)


class EndpointNotFoundException(Exception):
    """Raised when a requested endpoint is not found on the server."""

    def __init__(self, server_name: str, endpoint_name: str) -> None:
        super().__init__(
            f'Endpoint not found. Server was "{server_name}". Endpoint was "{endpoint_name}".'
        )
        self.endpoint_name = endpoint_name
