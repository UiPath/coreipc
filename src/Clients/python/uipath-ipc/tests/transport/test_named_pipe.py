"""Unit tests for the named-pipe transports.

These test the configurable knobs (pipe name, server name, computed paths)
and the macOS path rule, against the vectors in `src/MacPipePathVectors.json`
that the C# and TypeScript suites load too. The platform is injected, so they
run on any OS. End-to-end connectivity is covered by the integration tests
that talk to the real .NET sample server.
"""

from __future__ import annotations

import json
import os
import sys
from pathlib import Path

import pytest

from uipath_ipc import NamedPipeClientTransport, NamedPipeServerTransport
from uipath_ipc.transport.named_pipe import (
    MAC_MAX_PATH_BYTES,
    PIPE_ROOT_ENV_VAR,
    _posix_pipe_path,
    _truncate_utf8,
    default_mac_pipe_root,
    resolve_mac_pipe_path,
)

MAC_TMPDIR = "/var/folders/zz/zyxvpxvq6csfxvn_n0000000000000/T/"

_VECTORS_FILE = Path(__file__).resolve().parents[5] / "MacPipePathVectors.json"
VECTORS: list[dict] = json.loads(_VECTORS_FILE.read_text(encoding="utf-8"))["vectors"]


def _utf8_len(value: str) -> int:
    return len(value.encode("utf-8"))


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
    monkeypatch.setattr(sys, "platform", "linux")
    t = NamedPipeClientTransport(pipe_name="test")
    assert t._posix_address == os.path.join("/tmp", "CoreFxPipe_test")


def test_posix_address_honors_tmpdir(monkeypatch) -> None:
    """Linux interop: .NET binds under Path.GetTempPath(), which honors
    $TMPDIR — so must we, client AND server."""
    monkeypatch.setenv("TMPDIR", "/var/tmp/xy")
    monkeypatch.setattr(sys, "platform", "linux")
    assert NamedPipeClientTransport(pipe_name="test")._posix_address == os.path.join(
        "/var/tmp/xy", "CoreFxPipe_test"
    )
    assert NamedPipeServerTransport(pipe_name="test")._posix_address == os.path.join(
        "/var/tmp/xy", "CoreFxPipe_test"
    )


def test_posix_address_empty_tmpdir_falls_back_to_tmp(monkeypatch) -> None:
    monkeypatch.setenv("TMPDIR", "")
    monkeypatch.setattr(sys, "platform", "linux")
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


# --- macOS path rule -------------------------------------------------------


@pytest.mark.parametrize("vector", VECTORS, ids=[v["name"] for v in VECTORS])
def test_mac_rule_matches_shared_vector(vector: dict) -> None:
    actual = resolve_mac_pipe_path(
        vector["pipeName"], vector["rootEnv"], vector["defaultRoot"]
    )
    assert actual == vector["expected"], vector["name"]


def test_shared_vectors_cover_the_whole_rule() -> None:
    assert len(VECTORS) >= 8
    assert any(v["pipeName"].startswith("/") for v in VECTORS), "rooted passthrough"
    assert any(v["rootEnv"] for v in VECTORS), "env override"
    assert any(v["rootEnv"] is None for v in VECTORS), "default root"
    assert any(
        _utf8_len(v["defaultRoot"] + v["pipeName"]) > MAC_MAX_PATH_BYTES for v in VECTORS
    ), "truncation"
    assert any(
        _utf8_len(v["pipeName"]) != len(v["pipeName"]) for v in VECTORS
    ), "multi-byte truncation"


def test_mac_paths_fit_sun_path_and_carry_no_corefxpipe_prefix() -> None:
    for vector in VECTORS:
        assert _utf8_len(vector["expected"]) <= MAC_MAX_PATH_BYTES, vector["name"]
        assert "CoreFxPipe_" not in vector["expected"], vector["name"]


def test_truncate_utf8_keeps_short_values_and_never_splits_a_character() -> None:
    assert _truncate_utf8("abc", 3) == "abc"
    assert _truncate_utf8("abcd", 3) == "abc"
    assert _truncate_utf8("a\u00e9", 2) == "a"  # é is 2 bytes
    assert _truncate_utf8("a\u00e9", 3) == "a\u00e9"
    assert _truncate_utf8("a\U0001F680", 4) == "a"  # 🚀 is 4 bytes
    assert _truncate_utf8("a\U0001F680", 5) == "a\U0001F680"


def test_default_mac_pipe_root_is_tmpdir_for_now(monkeypatch) -> None:
    monkeypatch.setenv("TMPDIR", MAC_TMPDIR)
    assert default_mac_pipe_root() == MAC_TMPDIR
    monkeypatch.delenv("TMPDIR")
    assert default_mac_pipe_root() == "/tmp"
    monkeypatch.setenv("TMPDIR", "")
    assert default_mac_pipe_root() == "/tmp"


def test_on_mac_the_env_override_wins(monkeypatch) -> None:
    monkeypatch.setenv("TMPDIR", MAC_TMPDIR)
    monkeypatch.setenv(PIPE_ROOT_ENV_VAR, "/var/run/uipath")
    assert _posix_pipe_path("robot", platform="darwin") == "/var/run/uipath/robot"


def test_on_mac_the_default_root_is_used_when_env_is_unset_or_empty(monkeypatch) -> None:
    monkeypatch.setenv("TMPDIR", MAC_TMPDIR)
    monkeypatch.delenv(PIPE_ROOT_ENV_VAR, raising=False)
    assert _posix_pipe_path("robot", platform="darwin") == MAC_TMPDIR + "robot"
    monkeypatch.setenv(PIPE_ROOT_ENV_VAR, "")
    assert _posix_pipe_path("robot", platform="darwin") == MAC_TMPDIR + "robot"


def test_on_mac_a_rooted_name_is_used_verbatim(monkeypatch) -> None:
    monkeypatch.setenv(PIPE_ROOT_ENV_VAR, "/ignored")
    assert _posix_pipe_path("/custom/robot.sock", platform="darwin") == "/custom/robot.sock"


def test_on_mac_both_transports_go_through_the_rule(monkeypatch) -> None:
    monkeypatch.setattr(sys, "platform", "darwin")
    monkeypatch.setenv("TMPDIR", MAC_TMPDIR)
    monkeypatch.delenv(PIPE_ROOT_ENV_VAR, raising=False)
    long_name = "uipath-robot-service-pipe-0123456789abcdef0123456789abcdef01"
    expected = (MAC_TMPDIR + long_name)[:MAC_MAX_PATH_BYTES]
    assert NamedPipeClientTransport(pipe_name=long_name)._posix_address == expected
    assert NamedPipeServerTransport(pipe_name=long_name)._posix_address == expected


def test_off_mac_the_linux_mapping_is_unchanged(monkeypatch) -> None:
    """Linux keeps .NET's `$TMPDIR/CoreFxPipe_<name>`, even with the Mac override set."""
    monkeypatch.setenv("TMPDIR", "/var/tmp/xy")
    monkeypatch.setenv(PIPE_ROOT_ENV_VAR, "/var/run/uipath")
    assert _posix_pipe_path("robot", platform="linux") == os.path.join(
        "/var/tmp/xy", "CoreFxPipe_robot"
    )
