import asyncio

import pytest

from coreipc.dispatch.cancellation import CancellationToken, CancellationTokenSource


async def test_source_default_is_not_cancelled():
    cts = CancellationTokenSource()
    assert cts.is_cancellation_requested is False
    assert cts.token.is_cancelled is False


async def test_cancel_transitions_token_and_unblocks_waiters():
    cts = CancellationTokenSource()
    waiter = asyncio.create_task(cts.token.wait_cancelled())
    assert not waiter.done()
    cts.cancel()
    await asyncio.wait_for(waiter, timeout=1.0)
    assert cts.token.is_cancelled


async def test_throw_if_cancelled_raises_cancelled_error():
    cts = CancellationTokenSource()
    cts.cancel()
    with pytest.raises(asyncio.CancelledError):
        cts.token.throw_if_cancelled()


async def test_register_fires_callback_on_cancel():
    cts = CancellationTokenSource()
    hit = asyncio.Event()
    cts.token.register(hit.set)
    cts.cancel()
    await asyncio.wait_for(hit.wait(), timeout=1.0)


async def test_register_is_idempotent_when_already_cancelled():
    cts = CancellationTokenSource()
    cts.cancel()
    hit = asyncio.Event()
    cts.token.register(hit.set)
    # call_soon path: yield control once to let the callback fire.
    await asyncio.sleep(0)
    assert hit.is_set()


async def test_from_event_bridges_asyncio_event():
    event = asyncio.Event()
    cts = CancellationTokenSource.from_event(event)
    assert not cts.is_cancellation_requested
    event.set()
    # Give the bridge task a scheduling slot to propagate.
    for _ in range(5):
        if cts.is_cancellation_requested:
            break
        await asyncio.sleep(0)
    assert cts.is_cancellation_requested
