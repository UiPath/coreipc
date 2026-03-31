"""Named pipe server transport."""

from __future__ import annotations

import asyncio
import sys
from typing import Any

from ..base import ServerState, ServerTransport


class NamedPipeServerTransport(ServerTransport):
    """Server transport over named pipes.

    On Windows, uses Win32 named pipes (``\\\\.\\pipe\\PipeName``).
    On Linux/Mac, uses Unix domain sockets (``/tmp/CoreFxPipe_PipeName``).
    """

    def __init__(self, pipe_name: str) -> None:
        self.pipe_name = pipe_name

    async def create_server_state(self) -> ServerState:
        if sys.platform == "win32":
            return await _create_windows_state(self.pipe_name)
        else:
            return await _create_unix_state(self.pipe_name)

    def __str__(self) -> str:
        return f"ServerPipe={self.pipe_name}"


# -- Windows implementation --

class _WindowsNamedPipeServerState(ServerState):
    def __init__(self, pipe_name: str) -> None:
        self._pipe_name = pipe_name
        self._closed = False

    async def accept(self) -> tuple[Any, Any]:
        from ._pipe_stream import (
            windows_pipe_server_create,
            windows_pipe_server_wait,
            wrap_pipe_handle,
        )

        handle = await windows_pipe_server_create(self._pipe_name)
        try:
            await windows_pipe_server_wait(handle)
        except Exception:
            import win32file
            win32file.CloseHandle(handle)
            raise
        return wrap_pipe_handle(handle)

    async def close(self) -> None:
        self._closed = True


async def _create_windows_state(pipe_name: str) -> _WindowsNamedPipeServerState:
    return _WindowsNamedPipeServerState(pipe_name)


# -- Unix implementation (Unix domain sockets, matching .NET Core behavior) --

class _UnixNamedPipeServerState(ServerState):
    def __init__(
        self,
        server: asyncio.Server,
        queue: asyncio.Queue[tuple[asyncio.StreamReader, asyncio.StreamWriter]],
        path: str,
    ) -> None:
        self._server = server
        self._queue = queue
        self._path = path

    async def accept(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        return await self._queue.get()

    async def close(self) -> None:
        self._server.close()
        await self._server.wait_closed()
        import os
        try:
            os.unlink(self._path)
        except OSError:
            pass


async def _create_unix_state(pipe_name: str) -> _UnixNamedPipeServerState:
    import os

    path = f"/tmp/CoreFxPipe_{pipe_name}"

    # Clean up stale socket
    try:
        os.unlink(path)
    except OSError:
        pass

    queue: asyncio.Queue[tuple[asyncio.StreamReader, asyncio.StreamWriter]] = asyncio.Queue()

    def on_connection(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        queue.put_nowait((reader, writer))

    server = await asyncio.start_unix_server(on_connection, path=path)
    return _UnixNamedPipeServerState(server, queue, path)
