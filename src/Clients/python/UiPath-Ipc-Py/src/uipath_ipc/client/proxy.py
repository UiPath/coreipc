"""IpcProxy: dynamic proxy that intercepts attribute access and turns method calls into RPC calls."""

from __future__ import annotations

import inspect
from typing import Any, TYPE_CHECKING, get_type_hints

if TYPE_CHECKING:
    from .service_client import ServiceClient


class IpcProxy:
    """A dynamic proxy for a service contract.

    Intercepts method calls and routes them through the ServiceClient
    as RPC requests. Method signatures are introspected from the
    contract type's type hints to determine return types.
    """

    def __init__(self, service_client: ServiceClient, interface_type: type) -> None:
        # Use object.__setattr__ to avoid triggering __getattr__
        object.__setattr__(self, "_service_client", service_client)
        object.__setattr__(self, "_interface_type", interface_type)
        object.__setattr__(self, "_methods", _introspect_methods(interface_type))

    def __getattr__(self, name: str) -> Any:
        if name.startswith("_"):
            raise AttributeError(name)

        methods = object.__getattribute__(self, "_methods")
        if name not in methods:
            interface_type = object.__getattribute__(self, "_interface_type")
            raise AttributeError(
                f"{interface_type.__name__} has no method '{name}'."
            )

        return_type = methods[name]
        service_client = object.__getattribute__(self, "_service_client")

        async def caller(*args: Any, **kwargs: Any) -> Any:
            return await service_client.invoke(name, args, return_type)

        return caller


def _introspect_methods(interface_type: type) -> dict[str, type | None]:
    """Introspect an ABC/class to find its methods and their return types."""
    methods: dict[str, type | None] = {}

    try:
        hints = get_type_hints(interface_type)
    except Exception:
        hints = {}

    for name in dir(interface_type):
        if name.startswith("_"):
            continue
        attr = getattr(interface_type, name, None)
        if attr is None or not callable(attr):
            continue
        # Get return type from type hints or signature
        try:
            sig = inspect.signature(attr)
            ret = sig.return_annotation
            if ret is inspect.Parameter.empty:
                ret = hints.get("return")
            methods[name] = ret
        except (ValueError, TypeError):
            methods[name] = None

    return methods
