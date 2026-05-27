"""Service contracts (ABCs) for the playground.

Method names are PascalCase to match the .NET wire format.
"""

from abc import ABC, abstractmethod


class IComputingService(ABC):
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...

    @abstractmethod
    async def MultiplyInts(self, x: int, y: int) -> int: ...

    @abstractmethod
    async def Greet(self, name: str) -> str: ...
