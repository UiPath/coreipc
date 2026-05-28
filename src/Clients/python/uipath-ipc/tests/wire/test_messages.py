"""Round-trip tests for the wire/messages module.

Verifies:
  - MessageType enum values match the .NET wire bytes.
  - All DTOs round-trip through to_dict/from_dict and to_json/from_json.
  - The serialized JSON shape (PascalCase keys, optional fields, nullability)
    matches what a .NET server / client produces.
"""

from uipath_ipc.wire import (
    CancellationRequest,
    Error,
    MessageType,
    Request,
    Response,
)


# --- MessageType -----------------------------------------------------------

def test_message_type_values_match_dotnet() -> None:
    assert MessageType.REQUEST == 0
    assert MessageType.RESPONSE == 1
    assert MessageType.CANCELLATION_REQUEST == 2
    assert MessageType.UPLOAD_REQUEST == 3
    assert MessageType.DOWNLOAD_RESPONSE == 4


# --- Error -----------------------------------------------------------------

def test_error_minimal_round_trip() -> None:
    err = Error(message="boom")
    assert Error.from_dict(err.to_dict()) == err


def test_error_with_inner_round_trip() -> None:
    inner = Error(message="cause", type_name="System.InvalidOperationException")
    outer = Error(
        message="boom",
        stack_trace="at Foo.Bar()",
        type_name="System.AggregateException",
        inner_error=inner,
    )
    assert Error.from_dict(outer.to_dict()) == outer


def test_error_to_dict_uses_pascal_case_keys() -> None:
    err = Error(
        message="boom",
        stack_trace="...",
        type_name="System.Exception",
        inner_error=Error(message="cause"),
    )
    d = err.to_dict()
    assert set(d) == {"Message", "StackTrace", "Type", "InnerError"}
    assert set(d["InnerError"]) == {"Message", "StackTrace", "Type", "InnerError"}


# --- Request ---------------------------------------------------------------

def test_request_minimal_round_trip() -> None:
    req = Request(
        endpoint="IComputingService",
        method_name="AddFloats",
        parameters=["1.5", "2.5"],
    )
    assert Request.from_json(req.to_json()) == req


def test_request_full_round_trip() -> None:
    req = Request(
        endpoint="ISystemService",
        method_name="EchoString",
        parameters=['"hello"'],
        id="42",
        timeout_in_seconds=5.0,
    )
    assert Request.from_json(req.to_json()) == req


def test_request_parameters_are_already_json_encoded() -> None:
    """The wire format requires each parameter to be its own JSON string,
    not a raw Python value embedded in the array.
    """
    req = Request(
        endpoint="X",
        method_name="Y",
        parameters=["1.5", '"hi"', "true", "null"],
    )
    d = req.to_dict()
    assert d["Parameters"] == ["1.5", '"hi"', "true", "null"]


def test_request_matches_dotnet_wire_shape() -> None:
    captured = (
        '{"Endpoint":"IComputingService",'
        '"MethodName":"AddFloats",'
        '"Parameters":["1.5","2.5"],'
        '"Id":"0",'
        '"TimeoutInSeconds":5.0}'
    )
    req = Request.from_json(captured)
    assert req == Request(
        endpoint="IComputingService",
        method_name="AddFloats",
        parameters=["1.5", "2.5"],
        id="0",
        timeout_in_seconds=5.0,
    )


# --- Response --------------------------------------------------------------

def test_response_with_data_round_trip() -> None:
    resp = Response(request_id="42", data="4.0")
    assert Response.from_json(resp.to_json()) == resp


def test_response_with_error_round_trip() -> None:
    err = Error(message="boom", type_name="System.Exception")
    resp = Response(request_id="42", error=err)
    assert Response.from_json(resp.to_json()) == resp


def test_response_void_round_trip() -> None:
    """A response with neither data nor error (void return)."""
    resp = Response(request_id="42")
    assert Response.from_json(resp.to_json()) == resp


def test_response_matches_dotnet_wire_shape() -> None:
    captured = '{"RequestId":"0","Data":"4.0","Error":null}'
    resp = Response.from_json(captured)
    assert resp == Response(request_id="0", data="4.0", error=None)


# --- CancellationRequest ---------------------------------------------------

def test_cancellation_request_round_trip() -> None:
    cancel = CancellationRequest(request_id="42")
    assert CancellationRequest.from_json(cancel.to_json()) == cancel


def test_cancellation_request_matches_dotnet_wire_shape() -> None:
    captured = '{"RequestId":"0"}'
    cancel = CancellationRequest.from_json(captured)
    assert cancel == CancellationRequest(request_id="0")
