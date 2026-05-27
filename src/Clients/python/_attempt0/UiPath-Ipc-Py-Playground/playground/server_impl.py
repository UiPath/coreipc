"""Service implementations for the playground."""

from .contracts import IComputingService


class ComputingService(IComputingService):
    async def AddFloats(self, x: float, y: float) -> float:
        return x + y

    async def MultiplyInts(self, x: int, y: int) -> int:
        return x * y

    async def Greet(self, name: str) -> str:
        return f"Hello, {name}!"
