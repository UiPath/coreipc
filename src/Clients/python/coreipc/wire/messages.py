from __future__ import annotations

from dataclasses import dataclass, field
from enum import IntEnum
from typing import Optional


class MessageType(IntEnum):
    Request = 0
    Response = 1
    CancellationRequest = 2
    UploadRequest = 3
    DownloadResponse = 4


@dataclass
class Error:
    Message: str
    StackTrace: str
    Type: str
    InnerError: Optional["Error"] = None


@dataclass
class Request:
    Endpoint: str
    Id: str
    MethodName: str
    Parameters: list[str]
    TimeoutInSeconds: float = 0.0


@dataclass
class Response:
    RequestId: str
    Data: Optional[str] = None
    Error: Optional[Error] = None


@dataclass
class CancellationRequest:
    RequestId: str


WireMessage = Request | Response | CancellationRequest
