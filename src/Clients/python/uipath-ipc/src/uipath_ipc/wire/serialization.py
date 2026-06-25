"""Type-directed (de)serialization for contract arguments and results.

Plain JSON only round-trips ``str/int/float/bool/list/dict/None``. A
.NET/CoreIpc contract, though, uses value types JSON has no notion of —
``byte[]`` (base64), ``Guid``, ``DateTime``, ``decimal`` — and on .NET those
round-trip for free because Newtonsoft is handed ``typeof(TResult)``. This
module is the Python equivalent: encode those types on the way out, and
materialize a parsed result into the contract's declared return type on the
way back (the type comes from reflection on the method's return annotation).

Dispatch is by type and covers, in order: a **dataclass**, an **enum**, a
**scalar value type** (``bytes``/``UUID``/``datetime``/``Decimal``), a
**typing container** (``Optional``/``list``/``tuple``/``set``/``dict``), else
the value unchanged. The contract vocabulary is deliberately narrow — plain
JSON values and dataclasses — so the IPC layer never reaches into a consumer's
modeling framework (pydantic, ORM entities, …); map those at your own boundary.

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
import sys
import types
from decimal import Decimal
from typing import Any, Union, get_args, get_origin, get_type_hints
from uuid import UUID

_UNION_ORIGINS: tuple[object, ...] = (
    (Union, types.UnionType) if hasattr(types, "UnionType") else (Union,)
)


# --- outbound: argument -> JSON-able structure ----------------------------

def to_wire(value: Any) -> Any:
    """Encode an outgoing argument to a JSON-serializable structure, matching
    .NET's wire forms for the value types JSON can't represent."""
    # Enum BEFORE the primitive short-circuit: IntEnum / IntFlag and
    # `class E(str, Enum)` members are also str/int instances, so checking
    # primitives first would return the member itself and let the wire value
    # depend on json.dumps' incidental handling of mixin enums. Emit `.value`
    # explicitly (matching "enum -> numeric value" in LIMITATIONS.md).
    if isinstance(value, enum.Enum):
        return value.value
    if value is None or isinstance(value, (str, int, float, bool)):
        return value
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
    if isinstance(value, (_datetime.date, _datetime.time)):
        # date -> 'YYYY-MM-DD', time -> 'HH:MM:SS[.ffffff]' (.NET DateOnly /
        # TimeOnly). datetime already matched above, so this is date/time only.
        return value.isoformat()
    if isinstance(value, Decimal):
        if not value.is_finite():
            # NaN/Infinity have no .NET decimal representation — fail fast and
            # locally rather than emitting "NaN"/"Infinity" the peer rejects.
            raise ValueError(f"cannot serialize non-finite Decimal {value!r}")
        # As a string, not float(): preserves full precision. Newtonsoft reads a
        # JSON string into a decimal (it accepts scientific notation too);
        # from_wire decodes a JSON string or a number back to Decimal.
        return str(value)
    if dataclasses.is_dataclass(value) and not isinstance(value, type):
        return {
            f.name: to_wire(getattr(value, f.name))
            for f in dataclasses.fields(value)
        }
    if isinstance(value, (list, tuple, set, frozenset)):
        return [to_wire(v) for v in value]
    if isinstance(value, dict):
        # Values are encoded; keys are passed through. Only string keys are
        # supported (JSON keys are strings) — matching the .NET contracts, which
        # never use non-string-keyed dictionaries. A value-type key (UUID, etc.)
        # is left as-is and fails fast in json.dumps rather than being silently
        # transformed into a string we couldn't symmetrically decode.
        return {k: to_wire(v) for k, v in value.items()}
    return value


# --- inbound: parsed JSON -> declared type --------------------------------

def _normalize_dt_fraction(text: str) -> str:
    """Rewrite a datetime string's fractional seconds to EXACTLY 6 digits.

    .NET (Newtonsoft, ``FFFFFFF``) trims trailing zeros, so the wire carries a
    variable-length fraction (1–7 digits). Python 3.10's ``fromisoformat`` is
    strict — it accepts only a 3- or 6-digit fraction — so any other length
    (1/2/4/5/7) must be normalized: over-precision is truncated, short fractions
    are zero-padded, both to 6 digits (which 3.10 accepts). No-op if there's no
    fraction. The offset (``+hh:mm`` / ``-hh:mm``, already de-``Z``'d) is
    preserved. On 3.11+ this is only reached for 7-digit fractions; on 3.10 it
    handles every non-3/6 length."""
    head, dot, tail = text.partition(".")
    if not dot:
        return text
    frac, tz = tail, ""
    for sign in ("+", "-"):
        if sign in frac:
            frac, _, off = frac.partition(sign)
            tz = sign + off
            break
    return f"{head}.{(frac + '000000')[:6]}{tz}"


def _parse_datetime(value: Any) -> Any:
    if not isinstance(value, str):
        return value
    text = value
    if text.endswith("Z"):  # .NET/UTC 'Z' — fromisoformat needs an offset on <3.11
        text = text[:-1] + "+00:00"
    try:
        return _datetime.datetime.fromisoformat(text)
    except ValueError:
        if "." not in text:
            raise  # not a fractional-seconds problem — a genuinely bad value
        return _datetime.datetime.fromisoformat(_normalize_dt_fraction(text))


def _from_wire_dataclass(cls: type, data: Any) -> Any:
    if not isinstance(data, dict):
        return data
    hints = resolve_hints(cls)
    kwargs = {
        f.name: from_wire(data[f.name], hints.get(f.name, Any))
        for f in dataclasses.fields(cls)
        if f.name in data  # extra keys ignored (forward-compat); missing
    }                      # required fields make the ctor below raise.
    return cls(**kwargs)


def resolve_hints(obj: Any) -> dict:
    """``get_type_hints(obj)``, but resilient: if a single annotation can't be
    resolved at runtime (classically a name imported only under ``TYPE_CHECKING``),
    degrade ONLY that one to ``Any`` instead of dropping every hint. Stdlib
    ``get_type_hints`` is all-or-nothing — one unresolvable annotation would
    otherwise de-type a whole method (its params + one-way detection) or a whole
    dataclass. Shared by inbound dispatch and dataclass decoding."""
    try:
        return get_type_hints(obj)
    except Exception:
        pass
    # Per-name fallback: resolve each annotation in isolation.
    if isinstance(obj, type):
        raw: dict = {}
        for base in reversed(obj.__mro__):  # base-first so a subclass override wins
            raw.update(getattr(base, "__annotations__", {}))
        globalns = getattr(sys.modules.get(obj.__module__), "__dict__", {})
        localns: "dict | None" = dict(vars(obj))
    else:
        raw = getattr(obj, "__annotations__", {})
        globalns = getattr(obj, "__globals__", {})
        localns = None
    return {name: _resolve_one(ann, globalns, localns) for name, ann in raw.items()}


def _resolve_one(ann: Any, globalns: dict, localns: "dict | None") -> Any:
    try:
        value = eval(ann, globalns, localns) if isinstance(ann, str) else ann
    except Exception:
        return Any  # unresolvable (e.g. a TYPE_CHECKING-only name) — degrade just this one
    # get_type_hints maps a `None` annotation to NoneType; match it so one-way
    # (`-> None`) detection still works in the degraded path.
    return type(None) if value is None else value


def from_wire(parsed: Any, hint: Any) -> Any:
    """Materialize a parsed-JSON value into the declared `hint` type.

    A dataclass-typed `hint` is materialized into an instance — recursively, so
    nested dataclasses / ``list[Dataclass]`` / ``Optional[Dataclass]`` are built
    too. A missing required field makes the dataclass constructor raise
    (loud-on-drift, by design); give optional fields a default. A
    ``dict``/``Any``/unannotated `hint` passes the value through unchanged, so a
    contract that genuinely wants raw structures just declares ``dict``.

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
            return from_wire(parsed, non_none[0])  # Optional[X] / X | None -> X
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
                from_wire(x, args[i] if i < len(args) else Any)
                for i, x in enumerate(parsed)
            ]
        else:
            items = [from_wire(x, args[0]) for x in parsed]
        # Rebuild the declared container type (list stays a list). A set /
        # frozenset of unhashable elements (set[dict], set[SomeDataclass])
        # can't be rebuilt — degrade to a list rather than crash the call.
        if origin is list:
            return items
        try:
            return origin(items)
        except TypeError:
            return items
    if origin is dict and isinstance(parsed, dict):
        vt = args[1] if len(args) == 2 else Any
        return {k: from_wire(v, vt) for k, v in parsed.items()}

    if isinstance(hint, type):
        if issubclass(hint, enum.Enum):
            return hint(parsed)
        if hint in (bytes, bytearray):
            decoded = base64.b64decode(parsed)
            # b64decode always returns bytes; honor a `bytearray` hint.
            return bytearray(decoded) if hint is bytearray else decoded
        if hint is UUID:
            return UUID(parsed)
        if hint is _datetime.datetime:
            return _parse_datetime(parsed)
        if hint is _datetime.date:
            return _datetime.date.fromisoformat(parsed)
        if hint is _datetime.time:
            return _datetime.time.fromisoformat(parsed)
        if hint is Decimal:
            return Decimal(str(parsed))
        if dataclasses.is_dataclass(hint):
            return _from_wire_dataclass(hint, parsed)
    return parsed
