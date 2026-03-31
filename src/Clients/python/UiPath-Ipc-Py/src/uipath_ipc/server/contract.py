"""Service contract configuration: ContractSettings and ContractCollection."""

from __future__ import annotations

from typing import Any, Callable


class ContractSettings:
    """Configuration for a single service contract endpoint.

    Mirrors the .NET ContractSettings: associates a contract type with a
    service instance or factory, plus optional hooks.
    """

    def __init__(
        self,
        contract_type: type,
        instance: Any = None,
        factory: Callable[[], Any] | None = None,
        before_incoming_call: Callable[..., Any] | None = None,
    ) -> None:
        self.contract_type = contract_type
        self.instance = instance
        self.factory = factory
        self.before_incoming_call = before_incoming_call

    def get_service(self) -> Any:
        if self.instance is not None:
            return self.instance
        if self.factory is not None:
            return self.factory()
        raise RuntimeError(
            f"No service instance or factory configured for {self.contract_type.__name__}."
        )


class ContractCollection:
    """A collection of service contract endpoints.

    Supports adding contracts by type+instance, type+factory, or just type
    (to be resolved later via a factory).
    """

    def __init__(self) -> None:
        self._endpoints: list[ContractSettings] = []

    def add(
        self,
        contract_type: type,
        instance: Any = None,
        *,
        factory: Callable[[], Any] | None = None,
        before_incoming_call: Callable[..., Any] | None = None,
    ) -> ContractCollection:
        self._endpoints.append(
            ContractSettings(
                contract_type=contract_type,
                instance=instance,
                factory=factory,
                before_incoming_call=before_incoming_call,
            )
        )
        return self

    def __iter__(self):
        return iter(self._endpoints)

    def __len__(self) -> int:
        return len(self._endpoints)
