from __future__ import annotations

import json

from .messages import CancellationRequest, Error, MessageType, Request, Response, WireMessage


class UnsupportedMessageTypeError(Exception):
    pass


class CoreIpcCodec:
    """Wire codec matching UiPath.CoreIpc (Newtonsoft.Json, PascalCase, per-arg pre-stringified)."""

    def encode_request(self, request: Request) -> tuple[MessageType, bytes]:
        obj = {
            "Endpoint": request.Endpoint,
            "Id": request.Id,
            "MethodName": request.MethodName,
            "Parameters": list(request.Parameters),
            "TimeoutInSeconds": request.TimeoutInSeconds,
        }
        return MessageType.Request, _dumps(obj)

    def encode_response(self, response: Response) -> tuple[MessageType, bytes]:
        obj = {
            "RequestId": response.RequestId,
            "Data": response.Data,
            "Error": _error_to_dict(response.Error),
        }
        return MessageType.Response, _dumps(obj)

    def encode_cancel(self, cancel: CancellationRequest) -> tuple[MessageType, bytes]:
        obj = {"RequestId": cancel.RequestId}
        return MessageType.CancellationRequest, _dumps(obj)

    def decode(self, msg_type: MessageType, payload: bytes) -> WireMessage:
        if msg_type in (MessageType.UploadRequest, MessageType.DownloadResponse):
            raise UnsupportedMessageTypeError(
                f"Stream message type {msg_type.name} not supported in v1"
            )
        obj = json.loads(payload.decode("utf-8"))
        if msg_type == MessageType.Request:
            return Request(
                Endpoint=obj["Endpoint"],
                Id=obj["Id"],
                MethodName=obj["MethodName"],
                Parameters=list(obj.get("Parameters") or []),
                TimeoutInSeconds=float(obj.get("TimeoutInSeconds", 0.0)),
            )
        if msg_type == MessageType.Response:
            return Response(
                RequestId=obj["RequestId"],
                Data=obj.get("Data"),
                Error=_error_from_dict(obj.get("Error")),
            )
        if msg_type == MessageType.CancellationRequest:
            return CancellationRequest(RequestId=obj["RequestId"])
        raise UnsupportedMessageTypeError(f"Unknown MessageType: {msg_type!r}")


def _dumps(obj: object) -> bytes:
    # separators=(",", ":") — compact, matches Newtonsoft default (no spaces).
    # Newtonsoft emits floats with a decimal point; json.dumps does too for Python floats.
    return json.dumps(obj, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def _error_to_dict(error: Error | None) -> dict | None:
    if error is None:
        return None
    return {
        "Message": error.Message,
        "StackTrace": error.StackTrace,
        "Type": error.Type,
        "InnerError": _error_to_dict(error.InnerError),
    }


def _error_from_dict(obj: dict | None) -> Error | None:
    if obj is None:
        return None
    return Error(
        Message=obj.get("Message", ""),
        StackTrace=obj.get("StackTrace", ""),
        Type=obj.get("Type", ""),
        InnerError=_error_from_dict(obj.get("InnerError")),
    )
