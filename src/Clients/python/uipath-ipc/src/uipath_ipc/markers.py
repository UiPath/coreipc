"""Contract-method markers — documentation-only decorators that record
.NET-side method shape the Python signature can't express. No wire effect."""

from __future__ import annotations

from typing import Callable, TypeVar

_F = TypeVar("_F", bound=Callable[..., object])

#: Attribute set on a method decorated with :func:`ipc_cancellable`.
IPC_CANCELLABLE_ATTR = "__ipc_cancellable__"


def ipc_cancellable(method: _F) -> _F:
    """Mark a contract method whose .NET counterpart ends with a
    ``CancellationToken``.

    **Documentation-only — zero wire effect.** The Python signature omits the
    ``CancellationToken`` entirely: Python uses asyncio cancellation, delivered
    out-of-band as a ``CancellationRequest`` frame, never as a parameter. This
    marker simply records that the .NET method has a trailing ``CancellationToken``
    so the contract is self-describing — it explains why the Python signature has
    one fewer parameter than the C# one.

    Nothing reads this at call time. A Python *client* sends only the declared
    arguments; the .NET *server* fills the missing trailing ``CancellationToken``
    slot with a default and injects the real token by type (see
    ``Server.cs.GetArguments``). Symmetrically, a Python *server* hosting such a
    method ignores the trailing empty-string slot a .NET client sends for its
    ``CancellationToken``.

    The token MUST be the **last** .NET parameter. .NET itself accepts a
    ``CancellationToken`` at any position (detected by type), but a method-level
    marker can't carry position, so in a .NET↔Python pairing it is constrained to
    last — a non-last token would misalign the trailing arguments on the wire.

    Example::

        class IRobotService(ABC):
            @ipc_cancellable
            @abstractmethod
            async def LongRunning(self, count: int) -> int: ...
            # .NET: Task<int> LongRunning(int count, CancellationToken ct = default)
    """
    setattr(method, IPC_CANCELLABLE_ATTR, True)
    return method
