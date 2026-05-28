"""Unit tests for NamedPipeClientTransport.

These test the configurable knobs (pipe name, server name, computed paths).
End-to-end connectivity is covered by the integration tests that talk to
the real .NET sample server.
"""

from __future__ import annotations

import pytest

from uipath_ipc import NamedPipeClientTransport


def test_defaults_to_local_server() -> None:
    t = NamedPipeClientTransport(pipe_name="test")
    assert t.pipe_name == "test"
    assert t.server_name == "."


def test_explicit_server_name() -> None:
    t = NamedPipeClientTransport(pipe_name="test", server_name="REMOTE")
    assert t.server_name == "REMOTE"


def test_windows_address_format() -> None:
    t = NamedPipeClientTransport(pipe_name="test")
    assert t._windows_address == r"\\.\pipe\test"


def test_windows_address_with_remote_server() -> None:
    t = NamedPipeClientTransport(pipe_name="test", server_name="REMOTE")
    assert t._windows_address == r"\\REMOTE\pipe\test"


def test_posix_address_format() -> None:
    t = NamedPipeClientTransport(pipe_name="test")
    assert t._posix_address == "/tmp/CoreFxPipe_test"


def test_is_immutable() -> None:
    """frozen=True means assignment raises."""
    t = NamedPipeClientTransport(pipe_name="test")
    with pytest.raises(Exception):
        t.pipe_name = "other"  # type: ignore[misc]
