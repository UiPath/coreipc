import json

import pytest

from coreipc.wire.codec_coreipc import CoreIpcCodec, UnsupportedMessageTypeError
from coreipc.wire.messages import (
    CancellationRequest,
    Error,
    MessageType,
    Request,
    Response,
)


@pytest.fixture
def codec():
    return CoreIpcCodec()


def test_request_bytes_are_deterministic(codec):
    req = Request(
        Endpoint="IComputingService",
        Id="1",
        MethodName="AddFloats",
        Parameters=["1.5", "2.5"],
        TimeoutInSeconds=0.0,
    )
    mt, payload = codec.encode_request(req)
    assert mt == MessageType.Request
    # Keys in declaration order, compact separators, per-arg strings preserved verbatim.
    assert payload == (
        b'{"Endpoint":"IComputingService","Id":"1","MethodName":"AddFloats",'
        b'"Parameters":["1.5","2.5"],"TimeoutInSeconds":0.0}'
    )


def test_response_includes_nulls_like_newtonsoft(codec):
    mt, payload = codec.encode_response(Response(RequestId="1", Data="42", Error=None))
    assert mt == MessageType.Response
    obj = json.loads(payload)
    assert obj == {"RequestId": "1", "Data": "42", "Error": None}


def test_error_envelope_round_trip(codec):
    err = Error(
        Message="bad",
        StackTrace="at X",
        Type="System.Exception",
        InnerError=Error(Message="inner", StackTrace="", Type="System.Exception", InnerError=None),
    )
    mt, payload = codec.encode_response(Response(RequestId="1", Data=None, Error=err))
    decoded = codec.decode(mt, payload)
    assert isinstance(decoded, Response)
    assert decoded.Error.Message == "bad"
    assert decoded.Error.InnerError.Message == "inner"


def test_cancel_round_trip(codec):
    mt, payload = codec.encode_cancel(CancellationRequest(RequestId="7"))
    assert mt == MessageType.CancellationRequest
    assert codec.decode(mt, payload) == CancellationRequest(RequestId="7")


def test_decode_rejects_stream_message_types(codec):
    for mt in (MessageType.UploadRequest, MessageType.DownloadResponse):
        with pytest.raises(UnsupportedMessageTypeError):
            codec.decode(mt, b"{}")


def test_parameter_strings_are_passed_through(codec):
    """Each Parameter is already a JSON string — the codec must not re-escape it."""
    req = Request(
        Endpoint="I",
        Id="1",
        MethodName="M",
        Parameters=['{"nested":true}', '"raw"', "42"],
        TimeoutInSeconds=0.0,
    )
    _, payload = codec.encode_request(req)
    obj = json.loads(payload)
    assert obj["Parameters"] == ['{"nested":true}', '"raw"', "42"]
