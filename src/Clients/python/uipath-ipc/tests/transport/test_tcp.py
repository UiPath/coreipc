"""Unit tests for TcpClientTransport.

End-to-end connectivity is covered by the integration tests that talk to
the real .NET sample server. These tests cover the configurable knobs.
"""

from __future__ import annotations

import asyncio

import pytest

from uipath_ipc import TcpClientTransport


def test_constructor_stores_host_and_port() -> None:
    t = TcpClientTransport(host="127.0.0.1", port=5050)
    assert t.host == "127.0.0.1"
    assert t.port == 5050


def test_is_immutable() -> None:
    t = TcpClientTransport(host="127.0.0.1", port=5050)
    with pytest.raises(Exception):
        t.port = 9999  # type: ignore[misc]


async def test_connect_against_local_listener() -> None:
    """Loopback smoke test: spin up a TCP server, connect, exchange bytes."""

    received: list[bytes] = []

    async def handle(reader: asyncio.StreamReader, writer: asyncio.StreamWriter) -> None:
        data = await reader.readexactly(5)
        received.append(data)
        writer.write(b"pong")
        await writer.drain()
        writer.close()

    server = await asyncio.start_server(handle, host="127.0.0.1", port=0)
    host, port = server.sockets[0].getsockname()[:2]

    async with server:
        t = TcpClientTransport(host=host, port=port)
        reader, writer = await t.connect()
        try:
            writer.write(b"ping!")
            await writer.drain()
            reply = await reader.readexactly(4)
            assert reply == b"pong"
        finally:
            writer.close()
            try:
                await writer.wait_closed()
            except Exception:
                pass

    assert received == [b"ping!"]
