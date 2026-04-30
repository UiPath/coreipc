"""Test contracts loosely mirroring src/Clients/js/test/node/Contracts/IAlgebra.ts
and IArithmetic.ts. Message<T>-with-payload is deferred in Python v1, so the callback
method here passes plain args instead of a Message<number>."""

from coreipc import Message, operation, service
from coreipc.dispatch.cancellation import CancellationToken


@service
class IAlgebra:
    @operation
    async def MultiplySimple(self, x: int, y: int) -> int: ...

    @operation
    async def Sleep(self, milliseconds: int, ct: CancellationToken | None = None) -> bool: ...

    @operation
    async def DivideByZero(self, x: int) -> int: ...

    @operation
    async def CallBackSum(self, x: int, y: int, msg: Message | None = None) -> int:
        """Server calls the client's IArithmetic.Sum(x, y) and returns the result."""


@service
class IArithmetic:
    @operation
    async def Sum(self, x: int, y: int) -> int: ...
