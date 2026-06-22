"""Unit tests for IpcConnection using a fake stream pair."""

from __future__ import annotations

import asyncio
import logging
import struct

import pytest

from uipath_ipc.client import IpcConnection
from uipath_ipc.wire import MessageType, Request, Response


class _BufferWriter:
    """Stand-in for asyncio.StreamWriter — just accumulates bytes."""

    def __init__(self) -> None:
        self.buffer = bytearray()
        self._closed = False

    def write(self, data: bytes) -> None:
        self.buffer.extend(data)

    async def drain(self) -> None:
        pass

    def close(self) -> None:
        self._closed = True

    async def wait_closed(self) -> None:
        pass


class _BlockingDrainWriter(_BufferWriter):
    """Accepts bytes but its drain() never completes — a non-reading peer
    blocking on backpressure."""

    async def drain(self) -> None:
        await asyncio.Event().wait()


class _AbortableTransport:
    def __init__(self) -> None:
        self.aborted = False

    def abort(self) -> None:
        self.aborted = True


class _StalledCloseWriter(_BufferWriter):
    """Models a non-reading peer on the ProactorEventLoop: wait_closed() never
    completes (the graceful flush can't drain); transport.abort() forces it."""

    def __init__(self) -> None:
        super().__init__()
        self.transport = _AbortableTransport()

    async def wait_closed(self) -> None:
        await asyncio.Event().wait()  # never set — graceful close hangs forever


def _frame(msg_type: MessageType, payload: bytes) -> bytes:
    return struct.pack("<Bi", int(msg_type), len(payload)) + payload


def _frame_types(buf: bytes) -> list[int]:
    """The MessageType byte of every complete frame in a buffer."""
    out: list[int] = []
    i = 0
    while i + 5 <= len(buf):
        length = int.from_bytes(buf[i + 1 : i + 5], "little", signed=True)
        out.append(buf[i])
        i += 5 + length
    return out


def _response_frame(resp: Response) -> bytes:
    return _frame(MessageType.RESPONSE, resp.to_json().encode("utf-8"))


async def _make_connection(*, prefeed: bytes = b"") -> tuple[IpcConnection, asyncio.StreamReader, _BufferWriter]:
    reader = asyncio.StreamReader()
    if prefeed:
        reader.feed_data(prefeed)
    writer = _BufferWriter()
    conn = IpcConnection(reader, writer)  # type: ignore[arg-type]
    conn.start()
    return conn, reader, writer


# --- happy path -----------------------------------------------------------

async def test_send_one_request_and_get_response() -> None:
    conn, reader, _writer = await _make_connection()
    try:
        send_task = asyncio.create_task(
            conn.send_request(Request(
                endpoint="X", method_name="Y", parameters=[], id="1",
            ))
        )

        # Let send_request register the future, then deliver the response.
        await asyncio.sleep(0)
        reader.feed_data(_response_frame(Response(request_id="1", data="42")))

        resp = await asyncio.wait_for(send_task, timeout=1.0)
        assert resp == Response(request_id="1", data="42")
    finally:
        await conn.aclose()


async def test_concurrent_requests_resolved_out_of_order() -> None:
    conn, reader, _writer = await _make_connection()
    try:
        t1 = asyncio.create_task(
            conn.send_request(Request(endpoint="X", method_name="Y", parameters=[], id="1"))
        )
        t2 = asyncio.create_task(
            conn.send_request(Request(endpoint="X", method_name="Z", parameters=[], id="2"))
        )
        await asyncio.sleep(0)
        # Deliver response for id=2 first, then id=1
        reader.feed_data(_response_frame(Response(request_id="2", data="second")))
        reader.feed_data(_response_frame(Response(request_id="1", data="first")))

        r1 = await asyncio.wait_for(t1, timeout=1.0)
        r2 = await asyncio.wait_for(t2, timeout=1.0)
        assert r1.data == "first"
        assert r2.data == "second"
    finally:
        await conn.aclose()


# --- failure paths --------------------------------------------------------

async def test_stream_close_fails_pending_requests() -> None:
    conn, reader, _writer = await _make_connection()
    try:
        send_task = asyncio.create_task(
            conn.send_request(Request(endpoint="X", method_name="Y", parameters=[], id="1"))
        )
        await asyncio.sleep(0)

        # Simulate stream close mid-request — the receive loop hits IncompleteReadError.
        reader.feed_eof()

        with pytest.raises(asyncio.IncompleteReadError):
            await asyncio.wait_for(send_task, timeout=1.0)
    finally:
        await conn.aclose()


async def test_send_on_closed_connection_raises() -> None:
    conn, _reader, _writer = await _make_connection()
    await conn.aclose()
    with pytest.raises(ConnectionError):
        await conn.send_request(Request(endpoint="X", method_name="Y", parameters=[], id="1"))


# --- request id allocation ------------------------------------------------

async def test_next_id_increments() -> None:
    conn, _reader, _writer = await _make_connection()
    try:
        assert conn.next_id() == "1"
        assert conn.next_id() == "2"
        assert conn.next_id() == "3"
    finally:
        await conn.aclose()


# --- protocol hardening ----------------------------------------------------

async def test_stream_frame_fails_closed() -> None:
    """UPLOAD_REQUEST/DOWNLOAD_RESPONSE (streams, out of scope) are followed
    by raw bytes we can't consume — the connection must fail closed instead
    of silently desyncing."""
    conn, reader, _writer = await _make_connection()
    send_task = asyncio.create_task(
        conn.send_request(Request(endpoint="X", method_name="Y", parameters=[], id="1"))
    )
    await asyncio.sleep(0)
    reader.feed_data(_frame(MessageType.UPLOAD_REQUEST, b""))
    with pytest.raises(ValueError, match="unsupported message type"):
        await asyncio.wait_for(send_task, timeout=1.0)
    for _ in range(50):
        if conn.is_closed:
            break
        await asyncio.sleep(0)
    assert conn.is_closed


async def test_malformed_payload_is_dropped_not_fatal(caplog) -> None:
    """A frame with valid framing but bad *content* (invalid JSON) is logged and
    dropped — the connection stays up and unrelated in-flight calls are NOT
    collateral-failed (the frame was length-prefixed, so the stream stays
    aligned). A later valid frame still completes the pending call."""
    conn, reader, writer = await _make_connection()
    try:
        send_task = asyncio.create_task(conn.send_request(
            Request(endpoint="X", method_name="Y", parameters=[], id="1")
        ))
        for _ in range(10):
            await asyncio.sleep(0)
            if "1" in conn._pending:
                break
        with caplog.at_level(logging.WARNING, logger="uipath_ipc.client.connection"):
            reader.feed_data(_frame(MessageType.RESPONSE, b"not json"))
            for _ in range(10):
                await asyncio.sleep(0)
        assert not conn.is_closed          # connection survived the bad frame
        assert not send_task.done()        # unparseable id → caller not failed
        assert any("dropping malformed" in r.message for r in caplog.records)
        # A subsequent valid response still completes the call.
        reader.feed_data(_response_frame(Response(request_id="1", data='"ok"')))
        resp = await asyncio.wait_for(send_task, timeout=1.0)
        assert resp.data == '"ok"'
    finally:
        await conn.aclose()


async def test_malformed_response_with_recoverable_id_fails_that_caller() -> None:
    """A malformed RESPONSE whose RequestId is still readable fails THAT pending
    call (so it doesn't hang silently), while the connection stays up."""
    conn, reader, _writer = await _make_connection()
    try:
        send_task = asyncio.create_task(conn.send_request(
            Request(endpoint="X", method_name="Y", parameters=[], id="1")
        ))
        for _ in range(10):
            await asyncio.sleep(0)
            if "1" in conn._pending:
                break
        # Valid JSON, RequestId recoverable, but a truthy-yet-malformed nested
        # Error (missing required Message) → from_dict raises a KeyError AFTER
        # the id is readable, so the handler fails that exact caller.
        reader.feed_data(_frame(MessageType.RESPONSE, b'{"RequestId":"1","Error":{"X":1}}'))
        with pytest.raises(Exception):
            await asyncio.wait_for(send_task, timeout=1.0)
        assert not conn.is_closed  # one bad frame did NOT kill the connection
    finally:
        await conn.aclose()


async def test_malformed_request_with_recoverable_id_answers_with_error() -> None:
    """A malformed inbound REQUEST whose Id is still readable is answered with an
    Error (so the sender doesn't hang); the connection stays up. (The RESPONSE
    twin is covered above; this is the REQUEST side.)"""
    conn, reader, writer = await _make_connection()
    try:
        # Valid JSON, Id recoverable, but missing "Endpoint" → Request.from_dict
        # raises KeyError after the id is readable.
        reader.feed_data(_frame(
            MessageType.REQUEST, b'{"Id":"9","MethodName":"M","Parameters":[]}'
        ))
        for _ in range(50):
            await asyncio.sleep(0)
            if _frame_types(bytes(writer.buffer)):
                break
        buf = bytes(writer.buffer)
        assert _frame_types(buf) == [int(MessageType.RESPONSE)]
        length = int.from_bytes(buf[1:5], "little", signed=True)
        resp = Response.from_json(buf[5:5 + length].decode("utf-8"))
        assert resp.request_id == "9"
        assert resp.error is not None
        assert resp.error.type_name == "System.IO.InvalidDataException"
        assert not conn.is_closed
    finally:
        await conn.aclose()


async def test_malformed_cancellation_frame_is_dropped(caplog) -> None:
    """A malformed CANCELLATION frame is logged and dropped — it can't be
    correlated to a pending handler, so there's nothing to answer; the
    connection must survive (symmetric with the request/response branches)."""
    conn, reader, _writer = await _make_connection()
    try:
        with caplog.at_level(logging.WARNING, logger="uipath_ipc.client.connection"):
            reader.feed_data(_frame(MessageType.CANCELLATION_REQUEST, b"not json"))
            for _ in range(10):
                await asyncio.sleep(0)
        assert not conn.is_closed
        assert any("malformed CANCELLATION" in r.message for r in caplog.records)
    finally:
        await conn.aclose()


async def test_abandon_unblocks_receive_loop() -> None:
    """_abandon() (used by IpcClient.__del__ for a client dropped without
    aclose()) marks closed and closes the writer so the blocked receive loop
    ends — preventing the orphaned-task/fd leak."""
    conn, _reader, writer = await _make_connection()
    assert not conn.is_closed
    conn._abandon()
    assert conn.is_closed
    assert writer._closed  # writer was closed so readexactly unblocks


def test_handler_systemexit_answers_peer_then_propagates() -> None:
    """SystemExit/KeyboardInterrupt in a handler still answer the peer (its
    future must not hang) but re-raise — unlike plain exceptions they are not
    swallowed; asyncio then propagates them out of the event loop itself
    (which is exactly the 'process-termination signal escapes' semantics).
    Run the scenario in its own loop so the crash is observable."""

    class _Svc:
        # Value-returning (request/response) so the error response + fatal-signal
        # re-raise path is exercised; a `-> None` method would be one-way (acked
        # before the handler runs, no error response).
        async def Boom(self) -> bool:
            raise SystemExit(3)

    writer = _BufferWriter()

    async def scenario() -> None:
        reader = asyncio.StreamReader()
        conn = IpcConnection(reader, writer, callbacks={"ISvc": (_Svc, _Svc())})  # type: ignore[arg-type]
        conn.start()
        req = Request(endpoint="ISvc", method_name="Boom", parameters=[], id="9")
        reader.feed_data(_frame(MessageType.REQUEST, req.to_json().encode("utf-8")))
        await asyncio.sleep(5)  # never reached: the handler crashes the loop

    with pytest.raises(SystemExit):
        asyncio.run(scenario())

    # The peer still received an error RESPONSE before the signal propagated.
    assert len(writer.buffer) > 5
    assert writer.buffer[0] == int(MessageType.RESPONSE)
    resp = Response.from_json(bytes(writer.buffer[5:]).decode("utf-8"))
    assert resp.request_id == "9"
    assert resp.error is not None and resp.error.type_name == "SystemExit"


# --- send deadline --------------------------------------------------------

async def test_send_frame_timeout_tears_down_connection() -> None:
    """A non-reading peer blocks drain() forever. With a send_timeout the write
    is bounded; on expiry the connection is torn down (so the shared writer
    isn't wedged for every queued frame) and the caller sees a TimeoutError."""
    reader = asyncio.StreamReader()
    writer = _BlockingDrainWriter()
    conn = IpcConnection(reader, writer, send_timeout=0.2)  # type: ignore[arg-type]
    conn.start()
    try:
        with pytest.raises(asyncio.TimeoutError):
            await conn._send_frame(MessageType.REQUEST, b"hello")
        assert conn.is_closed
    finally:
        await conn.aclose()


async def test_best_effort_send_timeout_does_not_tear_down_connection() -> None:
    """A best-effort RESPONSE send that hits the send_timeout (non-reading peer)
    is abandoned gracefully — the connection stays up, unlike a request send.
    Mirrors .NET writing responses with CancellationToken.None."""
    reader = asyncio.StreamReader()
    writer = _BlockingDrainWriter()
    conn = IpcConnection(reader, writer, send_timeout=0.2)  # type: ignore[arg-type]
    conn.start()
    try:
        await conn._try_send_response(Response(request_id="1", data='"ok"'))
        assert not conn.is_closed  # the slow response did NOT kill the connection
    finally:
        await conn.aclose()


async def test_receive_loop_resolves_while_write_lock_is_held() -> None:
    """The receive loop is independent of the write path: an inbound response
    resolves a pending request even while a wedged send holds the write lock
    (backpressure on the outbound direction must not block reads)."""
    reader = asyncio.StreamReader()
    writer = _BlockingDrainWriter()
    conn = IpcConnection(reader, writer)  # type: ignore[arg-type]
    conn.start()
    try:
        # A send that acquires the write lock and blocks on drain() forever.
        blocked = asyncio.create_task(conn._send_frame(MessageType.REQUEST, b"x"))
        await asyncio.sleep(0)
        # Register a pending request as send_request would, then feed its response.
        fut = asyncio.get_running_loop().create_future()
        conn._pending["7"] = fut
        reader.feed_data(_response_frame(Response(request_id="7", data='"ok"')))
        resp = await asyncio.wait_for(fut, timeout=1.0)
        assert resp.data == '"ok"'
        assert not blocked.done()  # send still wedged, yet the read resolved
    finally:
        blocked.cancel()
        await conn.aclose()


# --- cancellation racing an arrived response ------------------------------

async def test_response_arriving_at_cancel_is_returned_not_discarded() -> None:
    """A successful response landing in the same loop tick as a cancellation
    must be returned (mirroring .NET's atomic arbitration), not discarded — and
    no stray CancellationRequest is sent for a call the peer already answered."""
    conn, reader, writer = await _make_connection()
    try:
        task = asyncio.create_task(conn.send_request(
            Request(endpoint="X", method_name="Y", parameters=[], id="1")
        ))
        # Let send_request register its pending future and reach `await fut`.
        for _ in range(10):
            await asyncio.sleep(0)
            if "1" in conn._pending:
                break
        fut = conn._pending["1"]
        # Race: the response is set AND the task is cancelled in the same tick,
        # before the awaiting coroutine resumes (a pending cancel otherwise wins).
        fut.set_result(Response(request_id="1", data='"ok"'))
        task.cancel()
        resp = await task  # must NOT raise CancelledError
        assert resp.data == '"ok"'
        # Give any (erroneously) scheduled cancellation send a chance to run.
        for _ in range(5):
            await asyncio.sleep(0)
        assert int(MessageType.CANCELLATION_REQUEST) not in _frame_types(bytes(writer.buffer))
    finally:
        await conn.aclose()


# --- malformed frame: fail closed -----------------------------------------

async def test_unsupported_message_type_fails_closed(caplog) -> None:
    """An unsupported (stream) frame type must tear the connection down rather
    than silently desync, fail any in-flight request, and log the failure."""
    conn, reader, writer = await _make_connection()
    try:
        send_task = asyncio.create_task(conn.send_request(
            Request(endpoint="X", method_name="Y", parameters=[], id="1")
        ))
        await asyncio.sleep(0)  # let the request register + write
        with caplog.at_level(logging.ERROR, logger="uipath_ipc.client.connection"):
            reader.feed_data(_frame(MessageType.UPLOAD_REQUEST, b"\x00"))
            with pytest.raises(Exception):
                await asyncio.wait_for(send_task, timeout=1.0)
        assert conn.is_closed
        assert any("receive loop failed" in r.message for r in caplog.records)
    finally:
        await conn.aclose()


# --- close callbacks ------------------------------------------------------

async def test_close_callback_fires_on_aclose() -> None:
    conn, _reader, _writer = await _make_connection()
    fired: list[IpcConnection] = []
    conn.add_close_callback(fired.append)
    await conn.aclose()
    assert fired == [conn]


async def test_close_callback_fires_only_once() -> None:
    conn, _reader, _writer = await _make_connection()
    fired: list[IpcConnection] = []
    conn.add_close_callback(fired.append)
    await conn.aclose()
    await conn.aclose()  # second close must not re-fire
    assert fired == [conn]


async def test_close_callback_fires_on_peer_disconnect() -> None:
    conn, reader, _writer = await _make_connection()
    fired: list[IpcConnection] = []
    conn.add_close_callback(fired.append)
    # Peer hangs up: receive loop ends and should notify close.
    reader.feed_eof()
    deadline = asyncio.get_running_loop().time() + 1.0
    while not fired:
        if asyncio.get_running_loop().time() > deadline:
            pytest.fail("close callback did not fire on peer disconnect")
        await asyncio.sleep(0.01)
    assert fired == [conn]
    await conn.aclose()


async def test_close_callback_added_after_close_fires_immediately() -> None:
    conn, _reader, _writer = await _make_connection()
    await conn.aclose()
    fired: list[IpcConnection] = []
    conn.add_close_callback(fired.append)
    assert fired == [conn]


# --- bytes on the wire ----------------------------------------------------

async def test_wire_format_is_request_frame() -> None:
    conn, reader, writer = await _make_connection()
    try:
        req = Request(endpoint="ISystemService", method_name="EchoString",
                      parameters=['"hi"'], id="1")
        send_task = asyncio.create_task(conn.send_request(req))
        await asyncio.sleep(0)

        # Inspect what was written
        assert len(writer.buffer) > 5
        msg_type_byte = writer.buffer[0]
        payload_len = int.from_bytes(writer.buffer[1:5], "little", signed=True)
        assert msg_type_byte == int(MessageType.REQUEST)
        assert payload_len == len(writer.buffer) - 5

        # Tidy up: deliver a response so send_task can finish
        reader.feed_data(_response_frame(Response(request_id="1", data="ok")))
        await asyncio.wait_for(send_task, timeout=1.0)
    finally:
        await conn.aclose()


# --- aclose teardown ------------------------------------------------------

async def test_aclose_aborts_instead_of_hanging_on_non_reading_peer() -> None:
    """aclose() must not block on a graceful flush a non-reading peer can stall
    forever (the ProactorEventLoop defers connection_lost until the write buffer
    drains). It aborts the transport instead of awaiting wait_closed() — matching
    .NET's synchronous Connection.Dispose() and the TS client's socket.destroy()."""
    reader = asyncio.StreamReader()
    writer = _StalledCloseWriter()
    conn = IpcConnection(reader, writer)  # type: ignore[arg-type]
    conn.start()
    # Without the fix, aclose() awaits the never-completing wait_closed() and
    # hangs (so wait_for would time out). With the fix it aborts and returns.
    await asyncio.wait_for(conn.aclose(), timeout=2.0)
    assert writer.transport.aborted  # forced closed rather than waiting on flush
