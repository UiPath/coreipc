from __future__ import annotations

from typing import Protocol, runtime_checkable

from .messages import CancellationRequest, MessageType, Request, Response, WireMessage


@runtime_checkable
class Codec(Protocol):
    """Swap seam between dispatch/connection and the wire format.

    Implementations must be pure: given a message, produce deterministic bytes;
    given bytes + MessageType, produce the message. No I/O here.
    """

    def encode_request(self, request: Request) -> tuple[MessageType, bytes]: ...
    def encode_response(self, response: Response) -> tuple[MessageType, bytes]: ...
    def encode_cancel(self, cancel: CancellationRequest) -> tuple[MessageType, bytes]: ...
    def decode(self, msg_type: MessageType, payload: bytes) -> WireMessage: ...
