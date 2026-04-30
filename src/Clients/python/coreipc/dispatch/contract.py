from __future__ import annotations

import inspect
import types
import typing
from dataclasses import dataclass, field
from typing import Any, Callable

_OPERATION_MARK = "__coreipc_operation__"
_CONTRACT_MARK = "__coreipc_contract__"


class Message:
    """Context-injected parameter marker.

    A service method parameter annotated as `Message` (or `Message | None`) is **not**
    serialized on the wire — on the server side it is populated from the connection
    context (carries `client` for callbacks and `request_timeout` from the Request).
    Mirrors C#'s `UiPath.Ipc.Message`.

    v1 does not yet support the `Message[T]`-with-payload variant used by some
    existing .NET/TS tests; add when we need it.
    """

    def __init__(self) -> None:
        self.client: Any | None = None
        self.request_timeout: float = 0.0


@dataclass
class OperationInfo:
    name: str
    params: list[tuple[str, Any]]  # (name, annotation)
    return_type: Any
    timeout_seconds: float = 0.0


@dataclass
class ContractInfo:
    name: str
    cls: type
    operations: dict[str, OperationInfo] = field(default_factory=dict)


def operation(
    fn: Callable | None = None,
    *,
    timeout_seconds: float | None = None,
) -> Callable:
    """Mark an async method as a remote operation.

    `timeout_seconds` sets a per-operation timeout (borrowed from GenericIPC's
    `[RpcTimeout]`); a falsy value means "use the framework default".
    """

    def apply(f: Callable) -> Callable:
        setattr(f, _OPERATION_MARK, {"timeout_seconds": timeout_seconds})
        return f

    return apply if fn is None else apply(fn)


def service(cls: type) -> type:
    """Register a class as a contract. Endpoint name == class name."""
    operations: dict[str, OperationInfo] = {}
    try:
        hints = typing.get_type_hints(cls)
    except Exception:
        hints = {}
    for attr_name in dir(cls):
        if attr_name.startswith("__"):
            continue
        fn = getattr(cls, attr_name, None)
        meta = getattr(fn, _OPERATION_MARK, None)
        if meta is None:
            continue
        sig = inspect.signature(fn)
        try:
            method_hints = typing.get_type_hints(fn)
        except Exception:
            method_hints = {}
        params: list[tuple[str, Any]] = []
        for p in list(sig.parameters.values())[1:]:  # skip self
            params.append((p.name, method_hints.get(p.name, p.annotation)))
        operations[attr_name] = OperationInfo(
            name=attr_name,
            params=params,
            return_type=method_hints.get("return", sig.return_annotation),
            timeout_seconds=meta.get("timeout_seconds") or 0.0,
        )
    setattr(cls, _CONTRACT_MARK, ContractInfo(name=cls.__name__, cls=cls, operations=operations))
    return cls


def get_contract_info(cls: type) -> ContractInfo:
    info = getattr(cls, _CONTRACT_MARK, None)
    if info is None:
        raise TypeError(f"{cls!r} is not decorated with @service")
    return info


def _is_union(ann: Any) -> bool:
    origin = typing.get_origin(ann)
    return origin is typing.Union or origin is types.UnionType


def is_message_annotation(ann: Any) -> bool:
    if ann is Message:
        return True
    if _is_union(ann):
        return Message in typing.get_args(ann)
    return False


def is_cancellation_annotation(ann: Any) -> bool:
    # Forward-declared to avoid a circular import at module load.
    from .cancellation import CancellationToken

    if ann is CancellationToken:
        return True
    if _is_union(ann):
        return CancellationToken in typing.get_args(ann)
    return False
