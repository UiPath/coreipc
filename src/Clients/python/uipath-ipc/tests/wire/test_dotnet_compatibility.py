"""Wire-shape tests focused on .NET compatibility.

The round-trip tests in `test_messages.py` verify that our serializer is
self-consistent. Those would have happily kept emitting JSON null for
TimeoutInSeconds forever — null round-trips back to None, and the unit
tests are blissfully unaware that .NET refuses to parse it.

These tests are different: each one asserts a literal property of the
*serialized* shape against the .NET-side schema (taken from
`src/UiPath.CoreIpc/Wire/Dtos.cs`). They fail when our wire output
diverges from what .NET will accept, before the integration suite
even has to run.

The .NET schema we're matching:

    internal record Request(string Endpoint, string Id, string MethodName,
                            string[] Parameters, double TimeoutInSeconds)
    internal record Response(string RequestId, string? Data = null,
                             Error? Error = null)
    record CancellationRequest(string RequestId)
    public record Error(string Message, string StackTrace, string Type,
                        Error? InnerError)

Note that .NET's `double` is NOT nullable — emitting null on a double
field makes Newtonsoft.Json drop the entire Request.
"""

from __future__ import annotations

import json

import pytest

from uipath_ipc.wire import (
    CancellationRequest,
    Error,
    Request,
    Response,
)


# --- Request --------------------------------------------------------------

def test_request_writes_exactly_the_dotnet_field_set() -> None:
    """No extra fields, no missing fields — keys match the .NET record exactly."""
    req = Request(endpoint="X", method_name="Y", parameters=[])
    d = req.to_dict()
    assert set(d) == {"Endpoint", "Id", "MethodName", "Parameters", "TimeoutInSeconds"}


def test_request_writes_field_types_matching_dotnet_schema() -> None:
    """Each field's JSON type must match the corresponding .NET property type."""
    req = Request(
        endpoint="IComputingService",
        method_name="AddFloats",
        parameters=["1.5", "2.5"],
        id="42",
        timeout_in_seconds=5.0,
    )
    d = req.to_dict()
    assert isinstance(d["Endpoint"], str)
    assert isinstance(d["Id"], str)
    assert isinstance(d["MethodName"], str)
    assert isinstance(d["Parameters"], list)
    for p in d["Parameters"]:
        assert isinstance(p, str), "each Parameter is a JSON-encoded string"
    # bool is a subclass of int in Python — reject it explicitly. .NET double
    # accepts JSON ints or floats; both deserialize cleanly.
    assert isinstance(d["TimeoutInSeconds"], (int, float))
    assert not isinstance(d["TimeoutInSeconds"], bool)


def test_request_timeout_in_seconds_is_never_null() -> None:
    """The .NET Request.TimeoutInSeconds is non-nullable double.

    Emitting null makes Newtonsoft.Json throw inside the positional-
    constructor binding ("cannot convert null → double"); the entire
    Request is rejected and the server drops the connection. This was
    the root cause of the original integration-test failures.
    """
    req = Request(endpoint="X", method_name="Y", parameters=[])
    d = req.to_dict()
    assert d["TimeoutInSeconds"] is not None
    assert d["TimeoutInSeconds"] == 0   # the .NET "no timeout, use default" sentinel


def test_request_parameters_stay_strings_even_for_complex_payloads() -> None:
    """Request.Parameters is `string[]` in .NET — each element must be a string,
    not a parsed JSON value."""
    req = Request(
        endpoint="X",
        method_name="Y",
        parameters=['{"I": 1.0, "J": 2.0}', "true", "null", "[1, 2, 3]"],
    )
    d = req.to_dict()
    for p in d["Parameters"]:
        assert isinstance(p, str), f"expected str, got {type(p).__name__}: {p!r}"


def test_request_to_json_is_valid_json() -> None:
    req = Request(endpoint="X", method_name="Y", parameters=["1.0"])
    # Round-trip through stdlib json — verifies we emit something parseable.
    parsed = json.loads(req.to_json())
    assert isinstance(parsed, dict)


# --- Response -------------------------------------------------------------

def test_response_writes_exactly_the_dotnet_field_set() -> None:
    resp = Response(request_id="0")
    d = resp.to_dict()
    assert set(d) == {"RequestId", "Data", "Error"}


def test_response_with_data_field_types_match_dotnet_schema() -> None:
    resp = Response(request_id="42", data="3.0")
    d = resp.to_dict()
    assert isinstance(d["RequestId"], str)
    assert isinstance(d["Data"], str)
    assert d["Error"] is None


def test_response_void_emits_both_optional_fields_as_null() -> None:
    """A void return (no data, no error) emits Data and Error as JSON null,
    matching Newtonsoft.Json's default behavior on nullable fields."""
    resp = Response(request_id="0")
    d = resp.to_dict()
    assert d["Data"] is None
    assert d["Error"] is None


# --- CancellationRequest -------------------------------------------------

def test_cancellation_request_writes_only_request_id() -> None:
    cancel = CancellationRequest(request_id="42")
    d = cancel.to_dict()
    assert set(d) == {"RequestId"}
    assert isinstance(d["RequestId"], str)
    assert d["RequestId"] == "42"


# --- Error ---------------------------------------------------------------

def test_error_writes_exactly_the_dotnet_field_set() -> None:
    err = Error(message="boom")
    d = err.to_dict()
    assert set(d) == {"Message", "StackTrace", "Type", "InnerError"}


def test_error_field_types_match_dotnet_schema() -> None:
    err = Error(
        message="boom",
        stack_trace="at Foo.Bar()",
        type_name="System.Exception",
        inner_error=Error(message="cause"),
    )
    d = err.to_dict()
    assert isinstance(d["Message"], str)
    assert isinstance(d["StackTrace"], str)
    assert isinstance(d["Type"], str)
    assert isinstance(d["InnerError"], dict)
    # Inner Error has the same shape recursively.
    assert set(d["InnerError"]) == {"Message", "StackTrace", "Type", "InnerError"}


def test_error_omits_no_keys_when_optional_fields_are_none() -> None:
    """Even when optional fields are missing on the Python side, the JSON
    shape always includes them as null — matching Newtonsoft.Json's default."""
    err = Error(message="boom")
    d = err.to_dict()
    assert d["StackTrace"] is None
    assert d["Type"] is None
    assert d["InnerError"] is None


# --- Property-based-style spot checks (literal byte sequences) -----------

@pytest.mark.parametrize(
    "req,expected_substrings",
    [
        (
            Request(endpoint="IComputingService", method_name="AddFloats",
                    parameters=["1.5", "2.5"]),
            ['"Endpoint": "IComputingService"', '"MethodName": "AddFloats"',
             '"Parameters": ["1.5", "2.5"]', '"TimeoutInSeconds": 0'],
        ),
        (
            Request(endpoint="ISystemService", method_name="EchoString",
                    parameters=['"hi"'], id="7", timeout_in_seconds=2.5),
            ['"Endpoint": "ISystemService"', '"MethodName": "EchoString"',
             '"Id": "7"', '"TimeoutInSeconds": 2.5'],
        ),
    ],
)
def test_request_json_contains_expected_substrings(req: Request, expected_substrings: list[str]) -> None:
    """Quick sanity that the serialized JSON literally contains the
    expected text for each field. Not a strict byte-equality check
    (key ordering varies across Python versions / json options), but
    catches obvious shape regressions."""
    s = req.to_json()
    for sub in expected_substrings:
        assert sub in s, f"expected substring {sub!r} not in {s!r}"
