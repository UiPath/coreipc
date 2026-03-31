"""Router: maps endpoint names to ContractSettings, mirroring Router.cs."""

from __future__ import annotations

from abc import ABC
from typing import TYPE_CHECKING

from ..errors import EndpointNotFoundException

if TYPE_CHECKING:
    from .contract import ContractCollection, ContractSettings


class Router:
    """Maps endpoint names (class names) to ContractSettings."""

    def __init__(self, config: dict[str, ContractSettings], debug_name: str = "") -> None:
        self._endpoints = config
        self._debug_name = debug_name

    def resolve(self, endpoint_name: str) -> ContractSettings:
        settings = self._endpoints.get(endpoint_name)
        if settings is None:
            raise EndpointNotFoundException(self._debug_name, endpoint_name)
        return settings

    @staticmethod
    def build_config(endpoints: ContractCollection) -> dict[str, ContractSettings]:
        """Build the endpoint-name -> ContractSettings mapping.

        Registers each contract type by its class name. Also registers
        any ABC parent class names (matching .NET's interface hierarchy registration).
        """
        result: dict[str, ContractSettings] = {}
        for settings in endpoints:
            cls = settings.contract_type
            # Register by the class's own name
            result[cls.__name__] = settings
            # Also register by ABC parent names (like .NET registers parent interfaces)
            for base in cls.__mro__:
                if base is cls or base is ABC or base is object:
                    continue
                if hasattr(base, "__abstractmethods__"):
                    result[base.__name__] = settings
        return result
