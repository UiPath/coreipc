"""Unit tests for type-directed (de)serialization (wire/serialization.py).

The layer is pydantic-backed: `to_wire` encodes via `to_jsonable_python`
(bytes -> base64), `from_wire` materializes via `TypeAdapter.validate_python`.
"""

from __future__ import annotations

import dataclasses
import datetime as dt
import enum
from decimal import Decimal
from typing import Optional
from uuid import UUID

import pytest
from pydantic import Base64Bytes, BaseModel, ValidationError

from uipath_ipc.wire import from_wire, to_wire


_GUID = "550e8400-e29b-41d4-a716-446655440000"


# --- scalar value types ----------------------------------------------------

def test_bytes_encode_base64_and_decode_via_base64bytes() -> None:
    # .NET byte[] is base64: outbound any bytes -> base64; inbound needs the
    # Base64Bytes annotation (plain `bytes` would be treated as UTF-8).
    assert to_wire(b"\x04\x03\x02\x01") == "BAMCAQ=="
    assert from_wire("BAMCAQ==", Base64Bytes) == b"\x04\x03\x02\x01"


def test_uuid_round_trip() -> None:
    assert to_wire(UUID(_GUID)) == _GUID
    assert from_wire(_GUID, UUID) == UUID(_GUID)


def test_datetime_round_trip_and_z_suffix() -> None:
    d = dt.datetime(2026, 6, 12, 10, 30, 0, tzinfo=dt.timezone.utc)
    assert from_wire(to_wire(d), dt.datetime) == d
    assert from_wire("2026-06-12T10:30:00Z", dt.datetime) == d


def test_decimal_round_trips_and_accepts_number() -> None:
    # pydantic serializes Decimal as a JSON string (precision-preserving;
    # .NET's Newtonsoft accepts string->decimal); it round-trips, and a
    # .NET-sent JSON *number* also materializes.
    assert from_wire(to_wire(Decimal("1.5")), Decimal) == Decimal("1.5")
    assert from_wire(2.5, Decimal) == Decimal("2.5")


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


# --- dataclass -------------------------------------------------------------

@dataclasses.dataclass
class _Person:
    FirstName: str
    LastName: str | None = None


def test_dataclass_extra_keys_ignored() -> None:
    assert from_wire(
        {"FirstName": "Ada", "LastName": "Lovelace", "Unknown": 1}, _Person
    ) == _Person("Ada", "Lovelace")


def test_dataclass_missing_required_raises() -> None:
    # snake_case keys don't match -> required FirstName absent -> the silent
    # key-mismatch footgun becomes a loud validation error.
    with pytest.raises(ValidationError):
        from_wire({"first_name": "Ada"}, _Person)


# --- pydantic model --------------------------------------------------------

class _PersonModel(BaseModel):
    FirstName: str
    LastName: str | None = None


def test_pydantic_model_round_trip() -> None:
    assert to_wire(_PersonModel(FirstName="Ada")) == {"FirstName": "Ada", "LastName": None}
    got = from_wire({"FirstName": "Ada", "LastName": "Lovelace"}, _PersonModel)
    assert isinstance(got, _PersonModel) and got.LastName == "Lovelace"


def test_pydantic_type_mismatch_raises() -> None:
    with pytest.raises(ValidationError):
        from_wire({"FirstName": 123}, _PersonModel)  # wrong type, strict-ish


# --- passthrough -----------------------------------------------------------

def test_any_and_no_hint_pass_through() -> None:
    from typing import Any

    assert from_wire({"I": 1.0}, Any) == {"I": 1.0}
    assert from_wire({"I": 1.0}, None) == {"I": 1.0}
