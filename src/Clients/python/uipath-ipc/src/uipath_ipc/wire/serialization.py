"""Type-directed (de)serialization for contract arguments and results.

Plain JSON only round-trips ``str/int/float/bool/list/dict/None``. A
.NET/CoreIpc contract uses value types JSON has no notion of — ``Guid``,
``DateTime``, ``decimal``, ``byte[]`` — and on .NET those round-trip because
Newtonsoft is handed ``typeof(TResult)``. This module is the Python
equivalent, built on **pydantic**: encode arguments to JSON-able values, and
materialize a parsed result into the contract's declared return type (the
type comes from reflection on the method's return annotation).

We depend on pydantic directly (``pydantic>=2,<3`` — a wide range so a
consumer can pin their own 2.x) rather than hand-rolling the type walk:
``pydantic.TypeAdapter`` validates/coerces into any declared type — pydantic
models, stdlib dataclasses, enums, ``Optional``/``list``/``dict``, and the
scalar value types — and surfaces missing-field / type-mismatch errors.

.NET serializes ``byte[]`` as **base64**, whereas pydantic's plain ``bytes``
is UTF-8. So: outbound, `to_wire` encodes any ``bytes`` as base64; inbound,
annotate a byte-array field/return as ``pydantic.Base64Bytes`` to decode it.
"""

from __future__ import annotations

import json
from functools import lru_cache
from typing import Any

from pydantic import TypeAdapter
from pydantic_core import to_json


@lru_cache(maxsize=None)
def _adapter(hint: Any) -> TypeAdapter:
    """One cached TypeAdapter per declared type (construction isn't free)."""
    return TypeAdapter(hint)


def to_wire(value: Any) -> Any:
    """Encode an outgoing argument to a JSON-serializable structure.

    Handles pydantic models, stdlib dataclasses, enums, and value types
    (``UUID``/``datetime``/``Decimal``, and ``bytes`` → base64 to match .NET
    ``byte[]``). A plain JSON value passes through unchanged.

    Routed through pydantic's JSON serializer (then back to Python objects)
    so the result is pure JSON primitives the caller can ``json.dumps`` —
    ``to_jsonable_python`` would leave e.g. ``Decimal`` as a ``Decimal``.
    """
    return json.loads(to_json(value, bytes_mode="base64", by_alias=True))


def from_wire(parsed: Any, hint: Any) -> Any:
    """Materialize a parsed-JSON value into the declared `hint` type via
    pydantic (validation included). ``None`` and ``Any`` / no hint pass
    through unchanged, so a loosely-typed contract keeps returning raw
    structures (and the consumer can decode them itself)."""
    if hint is None or hint is Any:
        return parsed
    return _adapter(hint).validate_python(parsed)
