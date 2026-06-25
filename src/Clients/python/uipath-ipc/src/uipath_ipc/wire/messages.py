"""Wire message DTOs for the UiPath.Ipc protocol.

Matches the .NET wire format:
  - Frame header: 5 bytes = [MessageType: uint8][PayloadLength: int32 LE]
  - Payload: UTF-8 JSON

Field names in Python are snake_case; the wire JSON uses PascalCase. The
mapping is explicit in `to_dict` / `from_dict`.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from enum import IntEnum
from typing import Any


class MessageType(IntEnum):
    """The 1-byte type tag in the frame header."""

    REQUEST = 0
    RESPONSE = 1
    CANCELLATION_REQUEST = 2
    UPLOAD_REQUEST = 3
    DOWNLOAD_RESPONSE = 4


@dataclass(frozen=True, slots=True)
class Error:
    """Error info returned inside a Response when a remote call fails.

    Mirrors the .NET `Error` shape: a message plus optional stack trace,
    fully-qualified exception type name, and a recursive inner error.
    """

    message: str
    stack_trace: str | None = None
    type_name: str | None = None  # JSON field name is "Type"
    inner_error: Error | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "Message": self.message,
            "StackTrace": self.stack_trace,
            "Type": self.type_name,
            "InnerError": self.inner_error.to_dict() if self.inner_error else None,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Error:
        inner = d.get("InnerError")
        return cls(
            message=d["Message"],
            stack_trace=d.get("StackTrace"),
            type_name=d.get("Type"),
            inner_error=cls.from_dict(inner) if inner else None,
        )


@dataclass(frozen=True, slots=True)
class Request:
    """A method-call request sent from client to server.

    `parameters` is a list of *already JSON-encoded* strings, one per
    method argument. For example, calling `Foo(1.5, "hi", True)` yields
    `parameters=['1.5', '"hi"', 'true']`. This matches the .NET wire
    format exactly.
    """

    endpoint: str
    method_name: str
    parameters: list[str]
    id: str = "0"
    timeout_in_seconds: float | None = None

    def to_dict(self) -> dict[str, Any]:
        # .NET's Request.TimeoutInSeconds is a non-nullable `double`, with 0
        # as the "no timeout, use server default" sentinel. Sending JSON null
        # makes Newtonsoft.Json throw on Request deserialization (it cannot
        # convert null → double) and the server drops the connection.
        return {
            "Endpoint": self.endpoint,
            "MethodName": self.method_name,
            "Parameters": list(self.parameters),
            "Id": self.id,
            "TimeoutInSeconds": self.timeout_in_seconds
            if self.timeout_in_seconds is not None
            else 0.0,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Request:
        timeout = d.get("TimeoutInSeconds")
        return cls(
            endpoint=d["Endpoint"],
            method_name=d["MethodName"],
            parameters=list(d["Parameters"]),
            id=d.get("Id", "0"),
            # 0 / 0.0 from the wire decodes to None — both mean "no timeout".
            timeout_in_seconds=None if timeout in (None, 0, 0.0) else timeout,
        )

    def to_json(self) -> str:
        return json.dumps(self.to_dict())

    @classmethod
    def from_json(cls, s: str) -> Request:
        return cls.from_dict(json.loads(s))


@dataclass(frozen=True, slots=True)
class Response:
    """A response sent from server to client.

    Either `data` (the JSON-encoded return value) or `error` is set.
    Both can be ``None`` when the call returned void / completed cleanly
    without payload.
    """

    request_id: str
    data: str | None = None
    error: Error | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "RequestId": self.request_id,
            "Data": self.data,
            "Error": self.error.to_dict() if self.error else None,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Response:
        err = d.get("Error")
        return cls(
            request_id=d["RequestId"],
            data=d.get("Data"),
            error=Error.from_dict(err) if err else None,
        )

    def to_json(self) -> str:
        return json.dumps(self.to_dict())

    @classmethod
    def from_json(cls, s: str) -> Response:
        return cls.from_dict(json.loads(s))


@dataclass(frozen=True, slots=True)
class CancellationRequest:
    """A hint to the server that an in-flight request should be cancelled."""

    request_id: str

    def to_dict(self) -> dict[str, Any]:
        return {"RequestId": self.request_id}

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> CancellationRequest:
        return cls(request_id=d["RequestId"])

    def to_json(self) -> str:
        return json.dumps(self.to_dict())

    @classmethod
    def from_json(cls, s: str) -> CancellationRequest:
        return cls.from_dict(json.loads(s))
