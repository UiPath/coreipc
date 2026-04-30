from __future__ import annotations

from dataclasses import dataclass


class RemoteError(Exception):
    """Raised client-side when a remote call returned an Error payload.

    Preserves the remote .NET exception type name and stack trace. The chain of
    InnerError values is exposed via __cause__ so tracebacks read naturally.
    """

    def __init__(self, wire_error) -> None:
        self.wire_error = wire_error
        self.remote_type: str = wire_error.Type
        self.remote_stack_trace: str = wire_error.StackTrace
        super().__init__(f"{wire_error.Type}: {wire_error.Message}")
        if wire_error.InnerError is not None:
            self.__cause__ = RemoteError(wire_error.InnerError)


class EndpointNotFoundError(RemoteError):
    pass


class IpcTimeoutError(TimeoutError):
    pass


# Convenience: .NET TaskCanceledException is idiomatically surfaced as asyncio.CancelledError
# — callers can `except asyncio.CancelledError`. No separate OperationCanceledError class
# needed in v1.


def classify(wire_error) -> RemoteError:
    """Return a RemoteError subclass matching the remote exception type when we recognise it."""
    t = wire_error.Type
    if t == "UiPath.Ipc.EndpointNotFoundException":
        return EndpointNotFoundError(wire_error)
    return RemoteError(wire_error)
