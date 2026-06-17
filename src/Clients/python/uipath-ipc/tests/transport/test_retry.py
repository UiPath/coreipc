"""Unit tests for the shared bounded connect-retry helper."""

from __future__ import annotations

import pytest

from uipath_ipc.transport._retry import CONNECT_RETRY_DELAYS, retry_connect


async def test_returns_first_success_without_retrying() -> None:
    calls = 0

    async def connect() -> str:
        nonlocal calls
        calls += 1
        return "ok"

    assert await retry_connect(connect, (ConnectionRefusedError,)) == "ok"
    assert calls == 1


async def test_retries_transient_then_succeeds() -> None:
    attempts = 0

    async def connect() -> str:
        nonlocal attempts
        attempts += 1
        if attempts < 3:
            raise ConnectionRefusedError("not yet")
        return "ok"

    assert await retry_connect(connect, (ConnectionRefusedError,)) == "ok"
    assert attempts == 3


async def test_non_transient_error_is_not_retried() -> None:
    calls = 0

    async def connect() -> str:
        nonlocal calls
        calls += 1
        raise ValueError("fatal")

    with pytest.raises(ValueError):
        await retry_connect(connect, (ConnectionRefusedError,))
    assert calls == 1  # raised immediately, not retried


async def test_raises_last_transient_when_ladder_exhausted() -> None:
    calls = 0

    async def connect() -> str:
        nonlocal calls
        calls += 1
        raise FileNotFoundError("never ready")

    with pytest.raises(FileNotFoundError):
        await retry_connect(connect, (FileNotFoundError,))
    assert calls == len(CONNECT_RETRY_DELAYS)
