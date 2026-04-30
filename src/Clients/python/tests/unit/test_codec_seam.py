"""Prove that Connection depends only on the Codec ABC — a pure in-memory codec can drive it."""

import asyncio
import socket

from coreipc.connection import Connection
from coreipc.wire.messages import (
    CancellationRequest,
    MessageType,
    Request,
    Response,
)


class FakeCodec:
    """Tag-length fake — encodes each message type as JSON-like but with a marker byte.

    Intentionally not wire-compatible with CoreIpc; only used to show the abstraction holds.
    """

    def __init__(self):
        import json

        self._json = json

    def encode_request(self, req):
        return MessageType.Request, self._json.dumps({
            "e": req.Endpoint, "i": req.Id, "m": req.MethodName,
            "p": list(req.Parameters), "t": req.TimeoutInSeconds,
        }).encode()

    def encode_response(self, resp):
        return MessageType.Response, self._json.dumps({
            "i": resp.RequestId, "d": resp.Data, "err": None,
        }).encode()

    def encode_cancel(self, c):
        return MessageType.CancellationRequest, self._json.dumps({"i": c.RequestId}).encode()

    def decode(self, mt, payload):
        obj = self._json.loads(payload.decode())
        if mt == MessageType.Request:
            return Request(
                Endpoint=obj["e"], Id=obj["i"], MethodName=obj["m"],
                Parameters=list(obj["p"]), TimeoutInSeconds=obj["t"],
            )
        if mt == MessageType.Response:
            return Response(RequestId=obj["i"], Data=obj["d"], Error=None)
        return CancellationRequest(RequestId=obj["i"])


async def _loopback_pair():
    rsock, wsock = socket.socketpair()
    loop = asyncio.get_running_loop()
    reader_a = asyncio.StreamReader()
    reader_b = asyncio.StreamReader()
    await loop.connect_accepted_socket(
        lambda: asyncio.StreamReaderProtocol(reader_a), rsock
    )
    transport_b, proto_b = await loop.create_connection(
        lambda: asyncio.StreamReaderProtocol(reader_b), sock=wsock
    )
    writer_b = asyncio.StreamWriter(transport_b, proto_b, None, loop)
    # We still need a writer for side A; create a second pair by wrapping the already-attached
    # transport via StreamReaderProtocol is non-trivial, so use two socketpairs — one per direction.
    return reader_a, writer_b


async def test_fake_codec_round_trip():
    # Use a full duplex connection via a pair of socketpairs wired head-to-head.
    codec = FakeCodec()
    a_sock, b_sock = socket.socketpair()
    loop = asyncio.get_running_loop()

    async def make_stream(sock):
        reader = asyncio.StreamReader()
        proto = asyncio.StreamReaderProtocol(reader)
        transport, _ = await loop.create_connection(lambda: proto, sock=sock)
        writer = asyncio.StreamWriter(transport, proto, reader, loop)
        return reader, writer

    a_r, a_w = await make_stream(a_sock)
    b_r, b_w = await make_stream(b_sock)

    async def handler(req, cancel_event):
        return Response(RequestId=req.Id, Data=f'"{req.MethodName}-ok"', Error=None)

    server_conn = Connection(b_r, b_w, codec, request_handler=handler, debug_name="fake-srv")
    client_conn = Connection(a_r, a_w, codec, debug_name="fake-clt")
    server_conn.start()
    client_conn.start()

    resp = await asyncio.wait_for(
        client_conn.remote_call(Request("I", "1", "Ping", [], 0.0)), timeout=3.0
    )
    assert resp.Data == '"Ping-ok"'

    await client_conn.close()
    await server_conn.close()
