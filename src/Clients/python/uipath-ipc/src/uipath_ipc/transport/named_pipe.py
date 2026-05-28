"""Named-pipe client transport.

Cross-platform:
  - Windows: connects to `\\\\<server>\\pipe\\<name>` via the ProactorEventLoop's
    `create_pipe_connection`.
  - POSIX: connects to a Unix Domain Socket at `/tmp/CoreFxPipe_<name>`, which
    is the location .NET's `NamedPipeClient` uses on Linux/macOS for cross-
    platform IPC.
"""

from __future__ import annotations

import asyncio
import sys
from dataclasses import dataclass

from .base import ClientTransport


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

    # When the .NET server is accepting connections it's also constantly
    # recycling pipe instances. There's a small window between one connection
    # being accepted and the next pipe instance being created during which
    # CreateFile fails with ERROR_FILE_NOT_FOUND. Retry briefly to ride it out.
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
        return await asyncio.open_unix_connection(self._posix_address)
