from __future__ import annotations

import sys
import threading
import time
from datetime import datetime
from typing import TextIO

from .wire.messages import Request, Response


class IpcTracer:
    """Observation hooks for IPC traffic.

    All methods have no-op defaults — subclasses override what they care about. Call sites
    must tolerate arbitrary exceptions from trace methods (Connection guards these).
    """

    def on_call_sent(self, request: Request) -> None:
        pass

    def on_return_received(self, response: Response) -> None:
        pass

    def on_cancel_sent(self, request_id: str) -> None:
        pass

    def on_request_received(self, request: Request) -> None:
        pass

    def on_response_sent(self, response: Response) -> None:
        pass

    def on_error(self, where: str, exception: BaseException) -> None:
        pass


_ANSI = {
    "reset": "\x1b[0m",
    "dim": "\x1b[2m",
    "green": "\x1b[32m",
    "cyan": "\x1b[36m",
    "yellow": "\x1b[33m",
    "red": "\x1b[31m",
    "magenta": "\x1b[35m",
}


class ConsoleTracer(IpcTracer):
    """Colourised trace to stderr. Inspired by GenericIPC's ConsoleRpcTracer."""

    def __init__(
        self,
        name: str = "ipc",
        *,
        stream: TextIO | None = None,
        color: bool | None = None,
    ) -> None:
        self._name = name
        self._stream = stream or sys.stderr
        if color is None:
            color = self._stream.isatty()
        self._color = color
        self._lock = threading.Lock()
        self._start = time.monotonic()

    def _write(self, kind: str, color: str, text: str) -> None:
        ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        prefix = f"[{ts} {self._name}] {kind:<6}"
        if self._color:
            prefix = _ANSI[color] + prefix + _ANSI["reset"]
        with self._lock:
            self._stream.write(f"{prefix} {text}\n")
            self._stream.flush()

    def on_call_sent(self, request: Request) -> None:
        self._write(
            "CALL",
            "cyan",
            f"--> #{request.Id} {request.Endpoint}.{request.MethodName}({','.join(request.Parameters)})",
        )

    def on_return_received(self, response: Response) -> None:
        if response.Error is not None:
            self._write("ERR", "red", f"<-- #{response.RequestId} {response.Error.Type}: {response.Error.Message}")
        else:
            data = response.Data if response.Data is not None else ""
            self._write("RET", "green", f"<-- #{response.RequestId} {data}")

    def on_cancel_sent(self, request_id: str) -> None:
        self._write("CANCEL", "yellow", f"--> #{request_id}")

    def on_request_received(self, request: Request) -> None:
        self._write(
            "RECV",
            "magenta",
            f"<-- #{request.Id} {request.Endpoint}.{request.MethodName}({','.join(request.Parameters)})",
        )

    def on_response_sent(self, response: Response) -> None:
        if response.Error is not None:
            self._write("SEND", "red", f"--> #{response.RequestId} ERR {response.Error.Type}")
        else:
            self._write("SEND", "green", f"--> #{response.RequestId} OK")

    def on_error(self, where: str, exception: BaseException) -> None:
        self._write("ERR", "red", f"{where}: {exception!r}")
