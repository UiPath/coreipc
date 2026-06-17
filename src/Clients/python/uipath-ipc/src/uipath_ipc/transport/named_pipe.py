"""Named-pipe client and server transports.

Cross-platform:
  - Windows: `\\\\<server>\\pipe\\<name>` via the ProactorEventLoop's
    `create_pipe_connection` (client) / `start_serving_pipe` (server).
  - POSIX: a Unix Domain Socket at `$TMPDIR/CoreFxPipe_<name>` (fallback
    `/tmp`), matching .NET's `Path.Combine(Path.GetTempPath(), "CoreFxPipe_")`
    — `GetTempPath()` honors `$TMPDIR`, which macOS always sets.
"""

from __future__ import annotations

import asyncio
import contextlib
import errno
import os
import sys
from dataclasses import dataclass

from ._retry import retry_connect
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
        return os.path.join(
            os.environ.get("TMPDIR") or "/tmp", f"CoreFxPipe_{self.pipe_name}"
        )

    async def _connect_windows(
        self,
    ) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        loop = asyncio.get_running_loop()

        async def attempt() -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
            reader = asyncio.StreamReader()
            protocol = asyncio.StreamReaderProtocol(reader)
            transport, _ = await loop.create_pipe_connection(  # type: ignore[attr-defined]
                lambda: protocol, self._windows_address
            )
            return reader, asyncio.StreamWriter(transport, protocol, reader, loop)

        # Windows transiently fails CreateFile with ERROR_FILE_NOT_FOUND between
        # accepting one connection and creating the next pipe instance.
        return await retry_connect(attempt, (FileNotFoundError,))

    async def _connect_posix(
        self,
    ) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        # FileNotFoundError: the server signalled readiness before its accept
        # loop bound the socket file. ConnectionRefusedError: a stale socket
        # file lingers with no listener (e.g. just before the server re-binds).
        return await retry_connect(
            lambda: asyncio.open_unix_connection(self._posix_address),
            (FileNotFoundError, ConnectionRefusedError),
        )


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


class _PosixUnixServerHandle:
    """Wraps the `asyncio.Server` from `start_unix_server` so closing it also
    removes the socket file. `asyncio.Server.close()` leaves the file behind,
    whereas .NET deletes its ``CoreFxPipe_<name>`` on dispose — without this,
    every POSIX shutdown leaves a stale socket for the next bind to trip over."""

    __slots__ = ("_server", "_path")

    def __init__(self, server: asyncio.AbstractServer, path: str) -> None:
        self._server = server
        self._path = path

    def close(self) -> None:
        self._server.close()
        with contextlib.suppress(FileNotFoundError):
            os.unlink(self._path)

    async def wait_closed(self) -> None:
        await self._server.wait_closed()


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
        return os.path.join(
            os.environ.get("TMPDIR") or "/tmp", f"CoreFxPipe_{self.pipe_name}"
        )

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
        path = self._posix_address
        # Probe before unlinking: a successful connect means a LIVE server owns
        # this name — don't yank its socket out from under it; fail loudly like
        # .NET's "address in use". A refused/absent socket is stale and safe to
        # remove so bind() succeeds.
        if await self._is_live(path):
            raise OSError(errno.EADDRINUSE, f"address already in use: {path}")
        with contextlib.suppress(FileNotFoundError):
            os.unlink(path)
        server = await asyncio.start_unix_server(
            lambda r, w: on_connection(r, w), path
        )
        return _PosixUnixServerHandle(server, path)

    @staticmethod
    async def _is_live(path: str) -> bool:
        """True iff something is currently accepting on the Unix socket `path`.
        A refused connection (stale file, no listener), a missing file, or any
        other connect error means 'not live'."""
        try:
            _, writer = await asyncio.open_unix_connection(path)
        except OSError:
            return False
        writer.close()
        with contextlib.suppress(Exception):
            await writer.wait_closed()
        return True
