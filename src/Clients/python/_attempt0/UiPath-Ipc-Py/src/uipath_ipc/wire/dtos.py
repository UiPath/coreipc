"""Wire protocol data types matching the .NET CoreIpc wire format."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from enum import IntEnum
from typing import Any


class MessageType(IntEnum):
    Request = 0
    Response = 1
    CancellationRequest = 2
    UploadRequest = 3
    DownloadResponse = 4


@dataclass
class Request:
    Endpoint: str
    Id: str
    MethodName: str
    Parameters: list[str]
    TimeoutInSeconds: float = 0.0

    def to_dict(self) -> dict[str, Any]:
        return {
            "Endpoint": self.Endpoint,
            "Id": self.Id,
            "MethodName": self.MethodName,
            "Parameters": self.Parameters,
            "TimeoutInSeconds": self.TimeoutInSeconds,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Request:
        return cls(
            Endpoint=d["Endpoint"],
            Id=d["Id"],
            MethodName=d["MethodName"],
            Parameters=d["Parameters"],
            TimeoutInSeconds=d.get("TimeoutInSeconds", 0.0),
        )

    def __str__(self) -> str:
        return f"{self.Endpoint} {self.MethodName} {self.Id}."


@dataclass
class Error:
    Message: str
    StackTrace: str
    Type: str
    InnerError: Error | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "Message": self.Message,
            "StackTrace": self.StackTrace,
            "Type": self.Type,
            "InnerError": self.InnerError.to_dict() if self.InnerError else None,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any] | None) -> Error | None:
        if d is None:
            return None
        return cls(
            Message=d["Message"],
            StackTrace=d["StackTrace"],
            Type=d["Type"],
            InnerError=cls.from_dict(d.get("InnerError")),
        )

    @classmethod
    def from_exception(cls, ex: BaseException) -> Error:
        import traceback

        return cls(
            Message=str(ex),
            StackTrace="".join(traceback.format_exception(type(ex), ex, ex.__traceback__)),
            Type=f"{type(ex).__module__}.{type(ex).__qualname__}",
            InnerError=cls.from_exception(ex.__cause__) if ex.__cause__ else None,
        )


@dataclass
class Response:
    RequestId: str
    Data: str | None = None
    Error: Error | None = None

    def to_dict(self) -> dict[str, Any]:
        return {
            "RequestId": self.RequestId,
            "Data": self.Data,
            "Error": self.Error.to_dict() if self.Error else None,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> Response:
        return cls(
            RequestId=d["RequestId"],
            Data=d.get("Data"),
            Error=Error.from_dict(d.get("Error")),
        )

    @classmethod
    def fail(cls, request: Request, ex: BaseException) -> Response:
        return cls(RequestId=request.Id, Error=Error.from_exception(ex))

    @classmethod
    def success(cls, request: Request, data: str) -> Response:
        return cls(RequestId=request.Id, Data=data)


@dataclass
class CancellationRequest:
    RequestId: str

    def to_dict(self) -> dict[str, Any]:
        return {"RequestId": self.RequestId}

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> CancellationRequest:
        return cls(RequestId=d["RequestId"])
