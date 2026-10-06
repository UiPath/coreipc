"""Ambient options for outgoing IPC calls.

The caller-side counterpart of the server's ``IpcContext``: it lets a contract that takes no
``Message`` parameter still carry a per-call deadline, so a POCO contract needs no transport
type in its signature.
"""

from __future__ import annotations

import contextlib
from contextvars import ContextVar
from dataclasses import dataclass
from typing import Iterator

__all__ = ["IpcCallOptions", "call_options", "current_call_options"]


@dataclass(frozen=True)
class IpcCallOptions:
    """Options applied to every call made on the current context."""

    #: Seconds before the caller gives up. 0 means "no override"; negative means no timeout
    #: (see ``INFINITE_REQUEST_TIMEOUT``), matching ``Message.request_timeout``.
    request_timeout: float = 0.0


_current: ContextVar[IpcCallOptions | None] = ContextVar(
    "uipath_ipc_call_options", default=None
)


def current_call_options() -> IpcCallOptions | None:
    """The options in force on this context, or None."""
    return _current.get()


@contextlib.contextmanager
def call_options(request_timeout: float) -> Iterator[IpcCallOptions]:
    """Apply ``request_timeout`` to the calls made in this block.

    Nests: the previous value is restored on exit. An explicit ``Message`` argument still wins.
    """
    options = IpcCallOptions(request_timeout=request_timeout)
    token = _current.set(options)
    try:
        yield options
    finally:
        _current.reset(token)
