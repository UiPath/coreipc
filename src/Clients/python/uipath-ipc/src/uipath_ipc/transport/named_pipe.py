"""Named-pipe client and server transports.

Cross-platform:
  - Windows: `\\\\<server>\\pipe\\<name>` via the ProactorEventLoop's
    `create_pipe_connection` (client) / `start_serving_pipe` (server).
  - POSIX: a Unix Domain Socket at `/tmp/CoreFxPipe_<name>`, which is the
    location .NET's `NamedPipe{Client,Server}` use on Linux/macOS for
    cross-platform IPC.
"""

from __future__ import annotations

import asyncio
import contextlib
import os
import sys
from dataclasses import dataclass

from .base import ClientTransport, ConnectionHandler, ServerHandle, ServerTransport


@dataclass(frozen=True, slots=True)
class NamedPipeClientTransport(ClientTransport):
    """Client transport over a named pipe.

    Attributes:
        pipe_name: The bare pipe name (e.g. `"test"`), without any prefix.
        server_name: The remote machine name on Windows. Defaults to `"."`
            (the local machine). Ignored on POSIX.
    """

    pipe_name: str
    server_name: str = "."

    async def connect(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        if sys.platform == "win32":
            return await self._connect_windows()
        return await self._connect_posix()

    @property
    def _windows_address(self) -> str:
        return rf"\\{self.server_name}\pipe\{self.pipe_name}"

    @property
    def _posix_address(self) -> str:
        return f"/tmp/CoreFxPipe_{self.pipe_name}"

    # Brief retry on FileNotFoundError to ride out two race windows:
    #   - Windows: between accepting one connection and creating the next
    #     pipe instance, CreateFile transiently fails with ERROR_FILE_NOT_FOUND.
    #   - POSIX: the .NET server signals readiness before its accept-loop has
    #     actually bound the Unix Domain Socket file at /tmp/CoreFxPipe_<name>.
    _CONNECT_RETRY_DELAYS = (0.0, 0.05, 0.1, 0.2, 0.5, 1.0)

    async def _connect_windows(
        self,
    ) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        loop = asyncio.get_running_loop()
        last: BaseException | None = None
        for delay in self._CONNECT_RETRY_DELAYS:
            if delay:
                await asyncio.sleep(delay)
            try:
                reader = asyncio.StreamReader()
                protocol = asyncio.StreamReaderProtocol(reader)
                transport, _ = await loop.create_pipe_connection(  # type: ignore[attr-defined]
                    lambda: protocol, self._windows_address
                )
                writer = asyncio.StreamWriter(transport, protocol, reader, loop)
                return reader, writer
            except FileNotFoundError as ex:
                last = ex
        assert last is not None
        raise last

    async def _connect_posix(
        self,
    ) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        last: BaseException | None = None
        for delay in self._CONNECT_RETRY_DELAYS:
            if delay:
                await asyncio.sleep(delay)
            try:
                return await asyncio.open_unix_connection(self._posix_address)
            except FileNotFoundError as ex:
                last = ex
        assert last is not None
        raise last


class _PipeServerHandle:
    """Wraps the list of `PipeServer` objects from `start_serving_pipe`.

    `PipeServer` has no awaitable close signal, so `wait_closed()` blocks on an
    Event set by `close()` — matching `asyncio.Server.wait_closed()` semantics
    (return once the listener has been closed). Without this, `wait_closed()`
    returns immediately and `IpcServer.serve_forever()` would not block.
    """

    __slots__ = ("_servers", "_closed")

    def __init__(self, servers: list) -> None:
        self._servers = servers
        self._closed = asyncio.Event()

    def close(self) -> None:
        for server in self._servers:
            server.close()
        self._closed.set()

    async def wait_closed(self) -> None:
        await self._closed.wait()


@dataclass(frozen=True, slots=True)
class NamedPipeServerTransport(ServerTransport):
    """Server transport over a named pipe.

    Listens on the local pipe ``pipe_name`` and invokes the connection
    handler for each accepted client. Multiple clients are served (the
    listener re-arms after each accept).

    Attributes:
        pipe_name: The bare pipe name (no prefix), matching the name a
            client passes to `NamedPipeClientTransport`.
    """

    pipe_name: str

    @property
    def _windows_address(self) -> str:
        return rf"\\.\pipe\{self.pipe_name}"

    @property
    def _posix_address(self) -> str:
        return f"/tmp/CoreFxPipe_{self.pipe_name}"

    async def serve(self, on_connection: ConnectionHandler) -> ServerHandle:
        if sys.platform == "win32":
            return await self._serve_windows(on_connection)
        return await self._serve_posix(on_connection)

    async def _serve_windows(self, on_connection: ConnectionHandler) -> ServerHandle:
        loop = asyncio.get_running_loop()

        def factory() -> asyncio.StreamReaderProtocol:
            reader = asyncio.StreamReader(loop=loop)
            return asyncio.StreamReaderProtocol(
                reader,
                lambda r, w: on_connection(r, w),
                loop=loop,
            )

        servers = await loop.start_serving_pipe(  # type: ignore[attr-defined]
            factory, self._windows_address
        )
        return _PipeServerHandle(servers)

    async def _serve_posix(self, on_connection: ConnectionHandler) -> ServerHandle:
        # A stale socket file from a previous run blocks bind(); remove it.
        with contextlib.suppress(FileNotFoundError):
            os.unlink(self._posix_address)
        return await asyncio.start_unix_server(
            lambda r, w: on_connection(r, w), self._posix_address
        )
