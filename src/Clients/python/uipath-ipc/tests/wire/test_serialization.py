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
from uipath_ipc.wire.serialization import _normalize_dt_fraction


# --- scalar value types ----------------------------------------------------

_GUID = "550e8400-e29b-41d4-a716-446655440000"


def test_bytes_round_trip() -> None:
    assert to_wire(b"\x04\x03\x02\x01") == base64.b64encode(b"\x04\x03\x02\x01").decode()
    assert from_wire("BAMCAQ==", bytes) == b"\x04\x03\x02\x01"


def test_bytearray_hint_returns_bytearray() -> None:
    # b64decode always returns bytes; a `bytearray` hint must be honored.
    got = from_wire("BAMCAQ==", bytearray)
    assert got == bytearray(b"\x04\x03\x02\x01") and isinstance(got, bytearray)


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


@pytest.mark.parametrize("frac,expected_us", [
    ("5", 500000),        # 1 digit  — Newtonsoft trims trailing zeros
    ("12", 120000),       # 2 digits
    ("123", 123000),      # 3 digits (3.10 accepts natively)
    ("1234", 123400),     # 4 digits
    ("12345", 123450),    # 5 digits
    ("123456", 123456),   # 6 digits (3.10 accepts natively)
    ("1234567", 123456),  # 7 digits — over-precision, truncated
])
def test_datetime_variable_fraction_lengths(frac: str, expected_us: int) -> None:
    """Every .NET fractional length must parse to the right microsecond — incl.
    the 1/2/4/5-digit lengths that Python 3.10's strict fromisoformat rejects."""
    got = from_wire(f"2026-06-12T10:30:00.{frac}Z", dt.datetime)
    assert got.microsecond == expected_us
    assert got.tzinfo == dt.timezone.utc


@pytest.mark.parametrize("text,expected", [
    ("2026-06-12T10:30:00.5+00:00",       "2026-06-12T10:30:00.500000+00:00"),  # pad 1->6
    ("2026-06-12T10:30:00.12-05:00",      "2026-06-12T10:30:00.120000-05:00"),  # pad + neg offset
    ("2026-06-12T10:30:00.1234567+00:00", "2026-06-12T10:30:00.123456+00:00"),  # truncate 7->6
    ("2026-06-12T10:30:00.5",             "2026-06-12T10:30:00.500000"),        # naive (no offset)
    ("2026-06-12T10:30:00+00:00",         "2026-06-12T10:30:00+00:00"),         # no fraction -> no-op
])
def test_normalize_dt_fraction_pads_and_truncates_to_six(text: str, expected: str) -> None:
    """Version-independent guard for the pad/truncate logic: on 3.11+ the parser
    is lenient, so the fallback isn't exercised for short fractions otherwise."""
    assert _normalize_dt_fraction(text) == expected


def test_naive_datetime_has_no_tzinfo() -> None:
    # .NET DateTimeKind.Unspecified -> no 'Z'/offset on the wire -> a naive
    # datetime (no tzinfo), not a coerced-to-UTC one.
    got = from_wire("2026-06-12T10:30:00", dt.datetime)
    assert got == dt.datetime(2026, 6, 12, 10, 30, 0)
    assert got.tzinfo is None


@pytest.mark.parametrize("bad", [
    "invalid-date",
    "2026-13-45T99:99:99",       # out-of-range, no fraction -> re-raised directly
    "2026-13-45T99:99:99.5",     # out-of-range WITH a fraction -> raised via the fallback
    "T10:30:00",                 # not a datetime at all
])
def test_datetime_invalid_format_raises(bad: str) -> None:
    # A genuinely malformed value must surface ValueError, not be swallowed —
    # covers both the no-fraction re-raise and the fraction-fallback path.
    with pytest.raises(ValueError):
        from_wire(bad, dt.datetime)


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


def test_set_of_unhashable_degrades_to_list_not_crash() -> None:
    # set[<unhashable>] can't be rebuilt; degrade to a list rather than raise.
    got = from_wire([{"a": 1}, {"a": 2}], set[dict])
    assert isinstance(got, list) and got == [{"a": 1}, {"a": 2}]


def test_date_and_time_round_trip() -> None:
    d = dt.date(2026, 6, 18)
    assert to_wire(d) == "2026-06-18"
    assert from_wire("2026-06-18", dt.date) == d
    t = dt.time(10, 30, 0)
    assert to_wire(t) == "10:30:00"
    assert from_wire("10:30:00", dt.time) == t


def test_non_finite_decimal_rejected() -> None:
    with pytest.raises(ValueError):
        to_wire(Decimal("NaN"))
    with pytest.raises(ValueError):
        to_wire(Decimal("Infinity"))


def test_dict_with_value_type_keys_stringifies() -> None:
    # A dict keyed by UUID/datetime would TypeError in json.dumps without
    # recursing keys; to_wire stringifies them (JSON keys are strings).
    assert to_wire({UUID(_GUID): 1}) == {_GUID: 1}


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


@dataclasses.dataclass
class _InnerReq:
    Required: str


@dataclasses.dataclass
class _OuterNested:
    Name: str
    Inner: _InnerReq


def test_nested_dataclass_missing_required_field_raises() -> None:
    # The loud-on-drift behavior must hold RECURSIVELY: a missing required field
    # in a NESTED dataclass still raises (the inner ctor does), not silently None.
    with pytest.raises(TypeError):
        from_wire({"Name": "x", "Inner": {}}, _OuterNested)


def test_dataclass_hint_non_dict_input_returns_unchanged() -> None:
    # from_wire(<non-dict>, SomeDataclass) can't materialize — it returns the
    # value unchanged rather than crashing (documented passthrough).
    assert from_wire("not-a-dict", _Person) == "not-a-dict"
    assert from_wire(123, _Person) == 123


# --- no framework reflection: a pydantic-shaped class is NOT special-cased --

class _ModelLike:
    """Exposes the pydantic v2 surface (model_fields/model_validate/model_dump)
    to prove the IPC layer does NOT sniff for it. Contract DTOs are plain JSON
    values or dataclasses — nothing else — so the wire layer never reaches into
    a consumer's modeling framework. Map IPC DTOs to your own validated/domain
    types at your boundary instead."""

    model_fields = {"x": None}

    def __init__(self, x: int) -> None:
        self.x = x

    @classmethod
    def model_validate(cls, data: dict) -> "_ModelLike":
        return cls(data["x"])

    def model_dump(self, **_: object) -> dict:
        return {"x": self.x}


def test_pydantic_shaped_class_gets_no_special_handling() -> None:
    # from_wire to a model-like hint returns the raw parsed value (no
    # model_validate call); to_wire returns the instance untouched (no
    # model_dump). A consumer that wants a real model maps it itself.
    assert from_wire({"x": 9}, _ModelLike) == {"x": 9}
    inst = _ModelLike(7)
    assert to_wire(inst) is inst


# --- passthrough vs materialization ----------------------------------------

def test_dict_and_unannotated_pass_through() -> None:
    # A `dict`/`Any`/None hint passes the raw structure through — that's how a
    # contract opts OUT of materialization (declare `dict`, not a dataclass).
    assert from_wire({"I": 1.0}, dict) == {"I": 1.0}
    assert from_wire({"I": 1.0}, None) == {"I": 1.0}


def test_dataclass_hint_materializes_to_instance() -> None:
    """A dataclass-typed hint yields an INSTANCE (not a raw dict): the proxy and
    inbound dispatch rely on this so a typed contract round-trips typed objects
    with no explicit decode step."""
    got = from_wire({"FirstName": "Ada", "LastName": "Lovelace"}, _Person)
    assert isinstance(got, _Person) and got == _Person("Ada", "Lovelace")
