"""Named pipe client transport."""

from __future__ import annotations

import asyncio
import sys
from typing import Any

from ..base import ClientTransport


class NamedPipeClientTransport(ClientTransport):
    """Client transport over named pipes.

    On Windows, connects to \\\\server_name\\pipe\\pipe_name.
    On Linux/Mac, connects to the Unix domain socket at /tmp/CoreFxPipe_pipe_name.
    """

    def __init__(self, pipe_name: str, server_name: str = ".") -> None:
        self.pipe_name = pipe_name
        self.server_name = server_name

    async def connect(self) -> tuple[Any, Any]:
        if sys.platform == "win32":
            from ._pipe_stream import windows_pipe_connect

            return await windows_pipe_connect(self.pipe_name, self.server_name)
        else:
            path = f"/tmp/CoreFxPipe_{self.pipe_name}"
            return await asyncio.open_unix_connection(path)

    def __str__(self) -> str:
        return f"ClientPipe={self.pipe_name}"
