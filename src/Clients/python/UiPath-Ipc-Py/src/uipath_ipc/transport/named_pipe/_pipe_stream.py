"""Cross-platform async named pipe stream wrapper.

Windows: uses the ProactorEventLoop's native pipe I/O (asyncio.StreamReader/StreamWriter).
         Falls back to pywin32 with thread-pool executor for the server accept loop.
Linux/Mac: uses Unix domain sockets (what .NET Core uses for named pipes on non-Windows).
"""

from __future__ import annotations

import asyncio
import sys

if sys.platform == "win32":
    import pywintypes
    import win32file
    import win32pipe


PIPE_PREFIX_UNIX = "/tmp/CoreFxPipe_"


def get_pipe_path(pipe_name: str, server_name: str = ".") -> str:
    """Get the platform-specific pipe path matching .NET conventions."""
    if sys.platform == "win32":
        return f"\\\\{server_name}\\pipe\\{pipe_name}"
    else:
        return f"{PIPE_PREFIX_UNIX}{pipe_name}"


# -- Windows client: use ProactorEventLoop's native pipe support --

async def windows_pipe_connect(
    pipe_name: str, server_name: str = "."
) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
    """Connect to a Windows named pipe server using native asyncio pipe I/O."""
    pipe_path = get_pipe_path(pipe_name, server_name)
    loop = asyncio.get_running_loop()

    reader = asyncio.StreamReader()
    protocol = asyncio.StreamReaderProtocol(reader)
    transport, _ = await loop.create_pipe_connection(lambda: protocol, pipe_path)
    writer = asyncio.StreamWriter(transport, protocol, reader, loop)

    return reader, writer


# -- Windows server: pywin32 for CreateNamedPipe + ConnectNamedPipe,
#    then wrap handle with ProactorEventLoop for async I/O --

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


def wrap_pipe_handle(handle: int) -> tuple[asyncio.StreamReader, asyncio.StreamWriter]:
    """Wrap a server-side pipe handle into asyncio StreamReader/StreamWriter.

    Uses the ProactorEventLoop to register the handle for native async I/O.
    """
    loop = asyncio.get_running_loop()
    reader = asyncio.StreamReader()
    protocol = asyncio.StreamReaderProtocol(reader)

    # The ProactorEventLoop can wrap an existing pipe handle
    transport = loop._make_duplex_pipe_transport(handle, protocol, extra={})
    writer = asyncio.StreamWriter(transport, protocol, reader, loop)

    return reader, writer
