"""Contract-method markers — documentation-only decorators that record
.NET-side method shape the Python signature can't express. No wire effect."""

from __future__ import annotations

from typing import Callable, TypeVar

_F = TypeVar("_F", bound=Callable[..., object])

#: Attribute set on a method decorated with :func:`ipc_cancellable`.
IPC_CANCELLABLE_ATTR = "__ipc_cancellable__"


def ipc_cancellable(method: _F) -> _F:
    """Mark a contract method whose .NET counterpart observes cancellation (its
    signature ends with a ``CancellationToken``).

    The marker gates **cancellation propagation**. When the local task awaiting
    such a call is cancelled — or its local deadline elapses — the client sends a
    ``CancellationRequest`` frame to the peer so it can cancel its in-flight
    handler. **Without the marker, a local cancellation stays local: no
    ``CancellationRequest`` is sent** — signalling a peer that has no
    ``CancellationToken`` to observe it would be pointless.

    It does NOT change the request itself. The Python signature omits the
    ``CancellationToken`` (cancellation rides out-of-band as its own frame, never
    as a parameter), and the request's ``Parameters`` are unaffected — no token
    slot is added. The .NET *server* fills the missing trailing token slot with a
    default and injects the real token by type; a Python *server* ignores the
    empty-string slot a .NET client sends for its token.

    The token must be the **last** .NET parameter. .NET matches it by type at any
    position, but the Python signature omits it, so a non-last token would
    misalign the trailing arguments on the wire.

    Example::

        class IRobotService(ABC):
            @ipc_cancellable
            @abstractmethod
            async def LongRunning(self, count: int) -> int: ...
            # .NET: Task<int> LongRunning(int count, CancellationToken ct = default)
    """
    setattr(method, IPC_CANCELLABLE_ATTR, True)
    return method
