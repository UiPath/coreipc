"""Ambient call options: a POCO contract can still carry a per-call deadline."""

import asyncio

import pytest

from uipath_ipc import IpcCallOptions, call_options, current_call_options


def test_absent_outside_any_scope():
    assert current_call_options() is None


def test_scope_sets_and_restores_and_nests():
    with call_options(request_timeout=7):
        assert current_call_options() == IpcCallOptions(request_timeout=7)
        with call_options(request_timeout=3):
            assert current_call_options() == IpcCallOptions(request_timeout=3)
        assert current_call_options() == IpcCallOptions(request_timeout=7)
    assert current_call_options() is None


def test_scope_restores_on_error():
    with pytest.raises(RuntimeError):
        with call_options(request_timeout=5):
            raise RuntimeError("boom")
    assert current_call_options() is None


async def test_does_not_leak_into_a_sibling_task():
    seen: list[object] = []

    async def sibling() -> None:
        seen.append(current_call_options())

    with call_options(request_timeout=9):
        # A task created inside the scope copies the context...
        inside = asyncio.create_task(sibling())
        await inside
    # ...one created outside it does not.
    await asyncio.create_task(sibling())

    assert seen[0] == IpcCallOptions(request_timeout=9)
    assert seen[1] is None
