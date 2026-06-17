"""Unit tests for type-directed (de)serialization (wire/serialization.py)."""

from __future__ import annotations

import base64
import dataclasses
import datetime as dt
import enum
from decimal import Decimal
from typing import Optional
from uuid import UUID

import pytest

from uipath_ipc.wire import from_wire, to_wire


# --- scalar value types ----------------------------------------------------

_GUID = "550e8400-e29b-41d4-a716-446655440000"


def test_bytes_round_trip() -> None:
    assert to_wire(b"\x04\x03\x02\x01") == base64.b64encode(b"\x04\x03\x02\x01").decode()
    assert from_wire("BAMCAQ==", bytes) == b"\x04\x03\x02\x01"


def test_uuid_round_trip() -> None:
    u = UUID(_GUID)
    assert to_wire(u) == _GUID
    assert from_wire(_GUID, UUID) == u


def test_datetime_round_trip_and_z_suffix() -> None:
    d = dt.datetime(2026, 6, 12, 10, 30, 0, tzinfo=dt.timezone.utc)
    # UTC is emitted with a trailing 'Z' (not '+00:00') so .NET parses it as
    # DateTimeKind.Utc.
    assert to_wire(d) == "2026-06-12T10:30:00Z"
    assert from_wire(to_wire(d), dt.datetime) == d
    # .NET/UTC 'Z' suffix (fromisoformat needs an offset before 3.11)
    assert from_wire("2026-06-12T10:30:00Z", dt.datetime) == d
    # .NET emits up to 7 fractional digits; we trim to microseconds
    got = from_wire("2026-06-12T10:30:00.1234567+00:00", dt.datetime)
    assert got.microsecond == 123456


def test_decimal_round_trip() -> None:
    # Encoded as a precision-preserving string (not float), decoded back from a
    # string or a .NET-sent JSON number.
    assert to_wire(Decimal("1.5")) == "1.5"
    assert from_wire("1.5", Decimal) == Decimal("1.5")
    assert from_wire(2.5, Decimal) == Decimal("2.5")


def test_large_decimal_keeps_precision_no_scientific_notation() -> None:
    big = Decimal("79228162514264337593543950335")
    assert to_wire(big) == "79228162514264337593543950335"  # not 7.92e28
    assert from_wire(to_wire(big), Decimal) == big


class _Color(enum.IntEnum):
    Red = 1
    Green = 2


def test_enum_round_trip() -> None:
    assert to_wire(_Color.Green) == 2
    assert from_wire(2, _Color) is _Color.Green


# --- containers ------------------------------------------------------------

def test_list_of_uuid() -> None:
    assert from_wire([_GUID], list[UUID]) == [UUID(_GUID)]


def test_optional_unwraps() -> None:
    assert from_wire(_GUID, Optional[UUID]) == UUID(_GUID)
    assert from_wire(None, Optional[UUID]) is None


def test_dict_value_type() -> None:
    assert from_wire({"a": _GUID}, dict[str, UUID]) == {"a": UUID(_GUID)}


def test_variadic_tuple_and_set_keep_their_type() -> None:
    got = from_wire([_GUID, _GUID], tuple[UUID, ...])
    assert got == (UUID(_GUID), UUID(_GUID)) and isinstance(got, tuple)
    s = from_wire([1, 2, 2], set[int])
    assert s == {1, 2} and isinstance(s, set)


def test_fixed_arity_tuple_decodes_each_position() -> None:
    # Heterogeneous tuple[UUID, str]: the str element must NOT be coerced with
    # the first member's type (the old code applied args[0] to every element).
    got = from_wire([_GUID, "USD"], tuple[UUID, str])
    assert got == (UUID(_GUID), "USD") and isinstance(got, tuple)


def test_multi_member_union_passes_through_undecoded() -> None:
    # Optional[X] still decodes; a genuine multi-member union is left raw
    # (ambiguous to materialize) — documented behavior, not a silent surprise.
    import typing

    assert from_wire(_GUID, Optional[UUID]) == UUID(_GUID)
    assert from_wire(_GUID, typing.Union[UUID, int]) == _GUID  # not decoded


# --- dataclass (public from_wire) ------------------------------------------

@dataclasses.dataclass
class _Person:
    FirstName: str
    LastName: str | None = None


def test_dataclass_nested_and_extra_keys_ignored() -> None:
    got = from_wire(
        {"FirstName": "Ada", "LastName": "Lovelace", "Unknown": 1}, _Person
    )
    assert got == _Person("Ada", "Lovelace")  # extra key ignored


def test_dataclass_missing_required_raises() -> None:
    # snake_case keys don't match -> required FirstName absent -> ctor raises,
    # so the silent-loss footgun becomes a loud error (for required fields).
    with pytest.raises(TypeError):
        from_wire({"first_name": "Ada"}, _Person)


# --- pydantic (duck-typed; no real pydantic dependency) --------------------

class _FakePydantic:
    """Stand-in exposing the pydantic v2 surface from_wire/to_wire detect."""

    model_fields = {"x": None}

    def __init__(self, x: int) -> None:
        self.x = x

    @classmethod
    def model_validate(cls, data: dict) -> "_FakePydantic":
        return cls(data["x"])

    def model_dump(self, **_: object) -> dict:
        return {"x": self.x}


def test_pydantic_duck_dispatch() -> None:
    assert to_wire(_FakePydantic(7)) == {"x": 7}
    out = from_wire({"x": 9}, _FakePydantic)
    assert isinstance(out, _FakePydantic) and out.x == 9


# --- passthrough / proxy gating --------------------------------------------

def test_dict_and_unannotated_pass_through() -> None:
    assert from_wire({"I": 1.0}, dict) == {"I": 1.0}
    assert from_wire({"I": 1.0}, None) == {"I": 1.0}


def test_proxy_gate_leaves_dataclasses_raw() -> None:
    """The proxy calls with materialize_dataclasses=False, so a dataclass
    return stays a raw dict (consumers decode it themselves)."""
    assert from_wire(
        {"FirstName": "Ada"}, _Person, materialize_dataclasses=False
    ) == {"FirstName": "Ada"}
