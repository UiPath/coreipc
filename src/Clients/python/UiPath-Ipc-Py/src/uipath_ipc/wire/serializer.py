"""JSON serialization compatible with the .NET CoreIpc wire format."""

from __future__ import annotations

import json
from typing import Any


def serialize_parameter(value: Any) -> str:
    """Serialize a single parameter value to a JSON string.

    Each parameter in the Parameters array is individually JSON-serialized.
    """
    return json.dumps(value)


def deserialize_parameter(json_str: str, type_hint: type | None = None) -> Any:
    """Deserialize a JSON string back to a Python object."""
    if not json_str:
        return None
    raw = json.loads(json_str)
    if type_hint is None:
        return raw
    if type_hint in (int, float, str, bool):
        return type_hint(raw)
    return raw


def serialize_message(obj: Any) -> bytes:
    """Serialize a wire message (Request/Response/CancellationRequest) to UTF-8 JSON bytes."""
    return json.dumps(obj.to_dict(), separators=(",", ":")).encode("utf-8")


def deserialize_message(data: bytes, cls: type) -> Any:
    """Deserialize UTF-8 JSON bytes to a wire message."""
    d = json.loads(data.decode("utf-8"))
    return cls.from_dict(d)
