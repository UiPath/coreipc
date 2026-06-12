"""Unit tests for NamedPipeClientTransport.

These test the configurable knobs (pipe name, server name, computed paths).
End-to-end connectivity is covered by the integration tests that talk to
the real .NET sample server.
"""

from __future__ import annotations

import os

import pytest

from uipath_ipc import NamedPipeClientTransport, NamedPipeServerTransport


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


def test_posix_address_format(monkeypatch) -> None:
    monkeypatch.delenv("TMPDIR", raising=False)
    t = NamedPipeClientTransport(pipe_name="test")
    assert t._posix_address == os.path.join("/tmp", "CoreFxPipe_test")


def test_posix_address_honors_tmpdir(monkeypatch) -> None:
    """macOS interop: .NET binds under Path.GetTempPath(), which honors
    $TMPDIR (always set on macOS) — so must we, client AND server."""
    monkeypatch.setenv("TMPDIR", "/var/folders/xy")
    assert NamedPipeClientTransport(pipe_name="test")._posix_address == os.path.join(
        "/var/folders/xy", "CoreFxPipe_test"
    )
    assert NamedPipeServerTransport(pipe_name="test")._posix_address == os.path.join(
        "/var/folders/xy", "CoreFxPipe_test"
    )


def test_posix_address_empty_tmpdir_falls_back_to_tmp(monkeypatch) -> None:
    monkeypatch.setenv("TMPDIR", "")
    assert NamedPipeClientTransport(pipe_name="test")._posix_address == os.path.join(
        "/tmp", "CoreFxPipe_test"
    )
    assert NamedPipeServerTransport(pipe_name="test")._posix_address == os.path.join(
        "/tmp", "CoreFxPipe_test"
    )


def test_is_immutable() -> None:
    """frozen=True means assignment raises."""
    t = NamedPipeClientTransport(pipe_name="test")
    with pytest.raises(Exception):
        t.pipe_name = "other"  # type: ignore[misc]
