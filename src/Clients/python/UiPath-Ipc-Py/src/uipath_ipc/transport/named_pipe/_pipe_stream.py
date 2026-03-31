"""Cross-platform async named pipe stream wrapper.

Windows: uses win32pipe/win32file via pywin32, wrapping blocking calls in an executor.
Linux/Mac: uses Unix domain sockets (what .NET Core uses for named pipes on non-Windows).
"""

from __future__ import annotations

import asyncio
import sys

if sys.platform == "win32":
    import pywintypes
    import win32file
    import win32pipe


PIPE_PREFIX_WINDOWS = r"\\.\pipe\\"
PIPE_PREFIX_UNIX = "/tmp/CoreFxPipe_"


def get_pipe_path(pipe_name: str, server_name: str = ".") -> str:
    """Get the platform-specific pipe path matching .NET conventions."""
    if sys.platform == "win32":
        return f"\\\\{server_name}\\pipe\\{pipe_name}"
    else:
        return f"{PIPE_PREFIX_UNIX}{pipe_name}"


class PipeStreamReader:
    """Async reader wrapping a Windows named pipe handle."""

    def __init__(self, handle: int, loop: asyncio.AbstractEventLoop) -> None:
        self._handle = handle
        self._loop = loop
        self._buffer = b""
        self._eof = False

    async def readexactly(self, n: int) -> bytes:
        while len(self._buffer) < n:
            if self._eof:
                raise asyncio.IncompleteReadError(self._buffer, n)
            chunk = await self._read_chunk(max(n - len(self._buffer), 4096))
            if not chunk:
                self._eof = True
                raise asyncio.IncompleteReadError(self._buffer, n)
            self._buffer += chunk
        result = self._buffer[:n]
        self._buffer = self._buffer[n:]
        return result

    async def _read_chunk(self, size: int) -> bytes:
        def _blocking_read() -> bytes:
            try:
                hr, data = win32file.ReadFile(self._handle, size)
                return data
            except pywintypes.error:
                return b""

        return await self._loop.run_in_executor(None, _blocking_read)


class PipeStreamWriter:
    """Async writer wrapping a Windows named pipe handle."""

    def __init__(self, handle: int, loop: asyncio.AbstractEventLoop) -> None:
        self._handle = handle
        self._loop = loop
        self._buffer = bytearray()

    def write(self, data: bytes) -> None:
        self._buffer.extend(data)

    async def drain(self) -> None:
        if not self._buffer:
            return
        data = bytes(self._buffer)
        self._buffer.clear()

        def _blocking_write() -> None:
            win32file.WriteFile(self._handle, data)

        await self._loop.run_in_executor(None, _blocking_write)

    def close(self) -> None:
        try:
            win32file.CloseHandle(self._handle)
        except Exception:
            pass

    async def wait_closed(self) -> None:
        pass


async def windows_pipe_server_create(pipe_name: str) -> int:
    """Create a Windows named pipe server instance and return the handle."""
    pipe_path = get_pipe_path(pipe_name)

    def _create() -> int:
        return win32pipe.CreateNamedPipe(
            pipe_path,
            win32pipe.PIPE_ACCESS_DUPLEX | win32file.FILE_FLAG_OVERLAPPED,
            win32pipe.PIPE_TYPE_BYTE | win32pipe.PIPE_READMODE_BYTE | win32pipe.PIPE_WAIT,
            win32pipe.PIPE_UNLIMITED_INSTANCES,
            0,  # out buffer size
            0,  # in buffer size
            0,  # default timeout
            None,  # security attributes
        )

    loop = asyncio.get_running_loop()
    return await loop.run_in_executor(None, _create)


async def windows_pipe_server_wait(handle: int) -> None:
    """Wait for a client to connect to the pipe."""
    loop = asyncio.get_running_loop()

    def _wait() -> None:
        win32pipe.ConnectNamedPipe(handle, None)

    await loop.run_in_executor(None, _wait)


async def windows_pipe_connect(
    pipe_name: str, server_name: str = "."
) -> tuple[PipeStreamReader, PipeStreamWriter]:
    """Connect to a Windows named pipe server."""
    pipe_path = get_pipe_path(pipe_name, server_name)
    loop = asyncio.get_running_loop()

    def _connect() -> int:
        return win32file.CreateFile(
            pipe_path,
            win32file.GENERIC_READ | win32file.GENERIC_WRITE,
            0,
            None,
            win32file.OPEN_EXISTING,
            0,
            None,
        )

    handle = await loop.run_in_executor(None, _connect)
    return PipeStreamReader(handle, loop), PipeStreamWriter(handle, loop)


def wrap_pipe_handle(handle: int) -> tuple[PipeStreamReader, PipeStreamWriter]:
    """Wrap an existing pipe handle into reader/writer pair."""
    loop = asyncio.get_running_loop()
    return PipeStreamReader(handle, loop), PipeStreamWriter(handle, loop)
