"""Type-directed (de)serialization for contract arguments and results.

Plain JSON only round-trips ``str/int/float/bool/list/dict/None``. A
.NET/CoreIpc contract, though, uses value types JSON has no notion of —
``byte[]`` (base64), ``Guid``, ``DateTime``, ``decimal`` — and on .NET those
round-trip for free because Newtonsoft is handed ``typeof(TResult)``. This
module is the Python equivalent: encode those types on the way out, and
materialize a parsed result into the contract's declared return type on the
way back (the type comes from reflection on the method's return annotation).

Dispatch is by type and covers, in order: a **pydantic model** (duck-typed
via ``model_validate`` / ``model_dump`` — uipath-ipc never imports pydantic,
so the consumer owns that dependency), a **dataclass**, an **enum**, a
**scalar value type** (``bytes``/``UUID``/``datetime``/``Decimal``), a
**typing container** (``Optional``/``list``/``tuple``/``set``/``dict``), else
the value unchanged.

`to_wire` is always safe to call — for a plain JSON value it's a no-op, so
existing primitive/dict/list arguments are untouched. `from_wire` only
transforms when the destination type asks for it; an unknown/``Any``/``dict``
destination passes through, so a consumer that does its own decoding (or
returns loose dicts) is never surprised.
"""

from __future__ import annotations

import base64
import dataclasses
import datetime as _datetime
import enum
import types
from decimal import Decimal
from typing import Any, Union, get_args, get_origin
from uuid import UUID

_UNION_ORIGINS: tuple[object, ...] = (
    (Union, types.UnionType) if hasattr(types, "UnionType") else (Union,)
)


def _is_pydantic_model(t: object) -> bool:
    """Duck-typed pydantic v2 BaseModel subclass — no import of pydantic."""
    return (
        isinstance(t, type)
        and hasattr(t, "model_validate")
        and hasattr(t, "model_fields")
    )


# --- outbound: argument -> JSON-able structure ----------------------------

def to_wire(value: Any) -> Any:
    """Encode an outgoing argument to a JSON-serializable structure, matching
    .NET's wire forms for the value types JSON can't represent."""
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
    if _is_pydantic_model(type(value)):
        return value.model_dump(mode="json", by_alias=True)
    if isinstance(value, enum.Enum):
        return value.value
    if isinstance(value, (bytes, bytearray)):
        return base64.b64encode(bytes(value)).decode("ascii")
    if isinstance(value, UUID):
        return str(value)
    if isinstance(value, _datetime.datetime):
        # Emit a trailing 'Z' for UTC (not '+00:00') so .NET's RoundtripKind
        # parses it as DateTimeKind.Utc rather than Local. Naive/offset values
        # are left as isoformat produces them.
        text = value.isoformat()
        return text[:-6] + "Z" if text.endswith("+00:00") else text
    if isinstance(value, Decimal):
        # As a string, not float(): float() loses precision and renders large
        # values in scientific notation, which .NET's decimal parser rejects.
        # Newtonsoft reads a JSON string into a decimal fine; from_wire decodes
        # both a JSON string and a JSON number back to Decimal.
        return str(value)
    if dataclasses.is_dataclass(value) and not isinstance(value, type):
        return {
            f.name: to_wire(getattr(value, f.name))
            for f in dataclasses.fields(value)
        }
    if isinstance(value, (list, tuple, set, frozenset)):
        return [to_wire(v) for v in value]
    if isinstance(value, dict):
        return {k: to_wire(v) for k, v in value.items()}
    return value


# --- inbound: parsed JSON -> declared type --------------------------------

def _parse_datetime(value: Any) -> Any:
    if not isinstance(value, str):
        return value
    text = value
    if text.endswith("Z"):  # .NET/UTC 'Z' — fromisoformat needs an offset on <3.11
        text = text[:-1] + "+00:00"
    try:
        return _datetime.datetime.fromisoformat(text)
    except ValueError:
        # Trim sub-microsecond fractional digits (.NET emits up to 7).
        if "." in text:
            head, _, tail = text.partition(".")
            frac = tail
            tz = ""
            for sign in ("+", "-"):
                if sign in frac:
                    frac, _, off = frac.partition(sign)
                    tz = sign + off
                    break
            head = f"{head}.{frac[:6]}{tz}"
            return _datetime.datetime.fromisoformat(head)
        raise


def _from_wire_dataclass(cls: type, data: Any) -> Any:
    if not isinstance(data, dict):
        return data
    hints = _resolve_hints(cls)
    kwargs = {
        f.name: from_wire(data[f.name], hints.get(f.name, Any))
        for f in dataclasses.fields(cls)
        if f.name in data  # extra keys ignored (forward-compat); missing
    }                      # required fields make the ctor below raise.
    return cls(**kwargs)


def _resolve_hints(cls: type) -> dict:
    import typing

    try:
        return typing.get_type_hints(cls)
    except Exception:
        return {}


def from_wire(parsed: Any, hint: Any, *, materialize_dataclasses: bool = True) -> Any:
    """Materialize a parsed-JSON value into the declared `hint` type.

    `materialize_dataclasses=False` leaves plain dataclasses (and dicts) as
    raw parsed structures — the proxy uses this so consumers that decode
    results themselves keep receiving dicts.

    A multi-member ``Union`` other than ``Optional[X]`` is intentionally left
    undecoded (it's ambiguous to materialize from one wire value).
    """
    if parsed is None or hint is None or hint is Any:
        return parsed

    origin = get_origin(hint)
    args = get_args(hint)
    if origin in _UNION_ORIGINS:  # Optional[X] / X | Y
        non_none = [a for a in args if a is not type(None)]
        if len(non_none) == 1:
            return from_wire(  # Optional[X] / X | None -> decode as X
                parsed, non_none[0], materialize_dataclasses=materialize_dataclasses
            )
        # A genuine multi-member union (e.g. `bytes | str`, `A | B`) is
        # ambiguous to materialize from one wire value — guessing risks
        # mis-decoding (a base64 string satisfies both `bytes` and `str`). By
        # design it passes through undecoded; narrow the contract or decode it
        # yourself if a concrete type is needed.
        return parsed
    if origin in (list, tuple, set, frozenset) and args:
        if not isinstance(parsed, (list, tuple)):
            return parsed  # wire value isn't a JSON array — leave it alone
        if origin is tuple and not (len(args) == 2 and args[1] is Ellipsis):
            # Fixed-arity tuple[X, Y, ...]: decode each element by its position.
            # (tuple[X, ...] is the variadic form, handled by the else branch.)
            items = [
                from_wire(
                    x,
                    args[i] if i < len(args) else Any,
                    materialize_dataclasses=materialize_dataclasses,
                )
                for i, x in enumerate(parsed)
            ]
        else:
            items = [
                from_wire(x, args[0], materialize_dataclasses=materialize_dataclasses)
                for x in parsed
            ]
        # Rebuild the declared container type (list stays a list).
        return items if origin is list else origin(items)
    if origin is dict and isinstance(parsed, dict):
        vt = args[1] if len(args) == 2 else Any
        return {
            k: from_wire(v, vt, materialize_dataclasses=materialize_dataclasses)
            for k, v in parsed.items()
        }

    if isinstance(hint, type):
        if _is_pydantic_model(hint):
            return hint.model_validate(parsed)
        if issubclass(hint, enum.Enum):
            return hint(parsed)
        if hint in (bytes, bytearray):
            return base64.b64decode(parsed)
        if hint is UUID:
            return UUID(parsed)
        if hint is _datetime.datetime:
            return _parse_datetime(parsed)
        if hint is Decimal:
            return Decimal(str(parsed))
        if materialize_dataclasses and dataclasses.is_dataclass(hint):
            return _from_wire_dataclass(hint, parsed)
    return parsed
