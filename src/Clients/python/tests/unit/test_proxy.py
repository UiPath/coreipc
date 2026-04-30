import pytest

from coreipc.dispatch.cancellation import CancellationToken, CancellationTokenSource
from coreipc.dispatch.contract import Message, operation, service
from coreipc.dispatch.errors import RemoteError
from coreipc.dispatch.proxy import build_proxy
from coreipc.wire.messages import Error, Response


@service
class IMath:
    @operation
    async def Add(self, x: int, y: int) -> int: ...

    @operation
    async def Void(self, x: int) -> None: ...

    @operation
    async def Slow(
        self, seconds: float, msg: Message | None = None, ct: CancellationToken | None = None
    ) -> None: ...


class FakeChannel:
    def __init__(self, responder):
        self._i = 0
        self._responder = responder
        self.last_request = None

    def next_request_id(self) -> str:
        self._i += 1
        return str(self._i)

    async def remote_call(self, request):
        self.last_request = request
        return await self._responder(request)


async def _ok(val: str):
    async def fn(req):
        return Response(RequestId=req.Id, Data=val, Error=None)

    return fn


async def test_args_are_json_stringified_individually():
    async def responder(req):
        return Response(RequestId=req.Id, Data="42", Error=None)

    ch = FakeChannel(responder)
    proxy = build_proxy(IMath, ch)
    assert await proxy.Add(3, 4) == 42
    assert ch.last_request.Parameters == ["3", "4"]
    assert ch.last_request.Endpoint == "IMath"
    assert ch.last_request.MethodName == "Add"


async def test_message_and_cancellation_parameters_are_not_sent():
    async def responder(req):
        return Response(RequestId=req.Id, Data="", Error=None)

    ch = FakeChannel(responder)
    proxy = build_proxy(IMath, ch)
    cts = CancellationTokenSource()
    await proxy.Slow(1.5, ct=cts.token)
    # Only the 'seconds' parameter should land on the wire.
    assert ch.last_request.Parameters == ["1.5"]


async def test_void_returns_none():
    async def responder(req):
        return Response(RequestId=req.Id, Data="", Error=None)

    proxy = build_proxy(IMath, FakeChannel(responder))
    assert await proxy.Void(1) is None


async def test_remote_error_is_raised_and_preserves_type():
    async def responder(req):
        return Response(
            RequestId=req.Id,
            Data=None,
            Error=Error(
                Message="bad",
                StackTrace="",
                Type="System.InvalidOperationException",
                InnerError=None,
            ),
        )

    proxy = build_proxy(IMath, FakeChannel(responder))
    with pytest.raises(RemoteError) as excinfo:
        await proxy.Add(1, 2)
    assert excinfo.value.remote_type == "System.InvalidOperationException"


async def test_proxy_attribute_error_for_unknown_method():
    proxy = build_proxy(IMath, FakeChannel(lambda r: None))
    with pytest.raises(AttributeError):
        _ = proxy.DoesNotExist
