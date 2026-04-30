from __future__ import annotations

import asyncio
import os
import sys

from .base import ClientTransport, OnConnection, ServerHandle, ServerTransport

_IS_WIN = sys.platform.startswith("win")


def _pipe_address(pipe_name: str, server_name: str = ".") -> str:
    """Resolve a pipe name to its platform-specific path.

    On Windows the canonical form is ``\\\\<server>\\pipe\\<name>``. On Linux/macOS the
    .NET library and the TypeScript port both map a named pipe to a Unix Domain Socket
    at ``$TMPDIR/CoreFxPipe_<name>`` (default ``/tmp/CoreFxPipe_<name>``); we match that
    path so a Python peer interoperates with C# and TS peers without translation.

    An absolute path is passed through unchanged on POSIX so callers can fully control
    the socket location (e.g. for tests in a temp directory).
    """
    if _IS_WIN:
        return rf"\\{server_name}\pipe\{pipe_name}"
    if pipe_name.startswith("/"):
        return pipe_name
    tmp = os.environ.get("TMPDIR", "/tmp")
    return os.path.join(tmp, f"CoreFxPipe_{pipe_name}")


class NamedPipeClientTransport(ClientTransport):
    def __init__(self, pipe_name: str, server_name: str = ".") -> None:
        self.pipe_name = pipe_name
        self.server_name = server_name

    async def connect(self) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
        address = _pipe_address(self.pipe_name, self.server_name)
        if _IS_WIN:
            loop = asyncio.get_running_loop()
            reader = asyncio.StreamReader(loop=loop)
            protocol = asyncio.StreamReaderProtocol(reader, loop=loop)
            transport, _ = await loop.create_pipe_connection(lambda: protocol, address)
            return reader, asyncio.StreamWriter(transport, protocol, reader, loop)
        return await asyncio.open_unix_connection(address)


class NamedPipeServerTransport(ServerTransport):
    def __init__(self, pipe_name: str, server_name: str = ".") -> None:
        self.pipe_name = pipe_name
        self.server_name = server_name

    async def serve(self, on_connection: OnConnection) -> ServerHandle:
        address = _pipe_address(self.pipe_name, self.server_name)
        if _IS_WIN:
            return await self._serve_windows(address, on_connection)
        return await self._serve_posix(address, on_connection)

    async def _serve_windows(self, address: str, on_connection: OnConnection) -> ServerHandle:
        loop = asyncio.get_running_loop()

        def factory() -> asyncio.StreamReaderProtocol:
            reader = asyncio.StreamReader(loop=loop)
            return asyncio.StreamReaderProtocol(
                reader, _wrap_on_conn(on_connection), loop=loop
            )

        servers = await loop.start_serving_pipe(factory, address)
        return _PipeServerHandle(servers, socket_path=None)

    async def _serve_posix(self, address: str, on_connection: OnConnection) -> ServerHandle:
        # Match .NET: stale socket files from a crashed peer are unlinked so a fresh
        # bind succeeds. We only unlink AF_UNIX sockets, never regular files.
        if os.path.exists(address):
            import stat

            try:
                if stat.S_ISSOCK(os.stat(address).st_mode):
                    os.unlink(address)
            except OSError:
                pass
        server = await asyncio.start_unix_server(_wrap_on_conn(on_connection), path=address)
        return _PipeServerHandle([server], socket_path=address)


def _wrap_on_conn(on_connection: OnConnection):
    async def handler(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        try:
            await on_connection(reader, writer)
        finally:
            try:
                writer.close()
                await writer.wait_closed()
            except Exception:
                pass

    return handler


class _PipeServerHandle(ServerHandle):
    def __init__(self, servers: list, *, socket_path: str | None) -> None:
        self._servers = servers
        self._socket_path = socket_path
        self._closed = asyncio.Event()

    async def close(self) -> None:
        for s in self._servers:
            try:
                s.close()
            except Exception:
                pass
        if self._socket_path is not None:
            try:
                if os.path.exists(self._socket_path):
                    os.unlink(self._socket_path)
            except OSError:
                pass
        self._closed.set()

    async def wait_closed(self) -> None:
        await self._closed.wait()
