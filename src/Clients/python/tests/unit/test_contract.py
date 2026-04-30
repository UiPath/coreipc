import pytest

from coreipc.dispatch.cancellation import CancellationToken
from coreipc.dispatch.contract import (
    Message,
    get_contract_info,
    is_cancellation_annotation,
    is_message_annotation,
    operation,
    service,
)


def test_service_captures_operations_and_endpoint_name():
    @service
    class IComputingService:
        @operation
        async def AddFloats(self, x: float, y: float) -> float: ...

        @operation(timeout_seconds=5.0)
        async def Slow(self, ms: int) -> None: ...

        async def NotAnOperation(self): ...  # Should be ignored

    info = get_contract_info(IComputingService)
    assert info.name == "IComputingService"
    assert set(info.operations) == {"AddFloats", "Slow"}
    assert info.operations["Slow"].timeout_seconds == 5.0
    assert info.operations["AddFloats"].return_type is float


def test_get_contract_info_rejects_undecorated_class():
    class Plain: ...

    with pytest.raises(TypeError):
        get_contract_info(Plain)


def test_recognises_message_and_cancellation_parameters():
    @service
    class S:
        @operation
        async def Op(self, x: int, msg: Message | None = None, ct: CancellationToken | None = None) -> int: ...

    op = get_contract_info(S).operations["Op"]
    names_to_anns = dict(op.params)
    assert is_message_annotation(names_to_anns["msg"])
    assert is_cancellation_annotation(names_to_anns["ct"])
    assert not is_message_annotation(names_to_anns["x"])
    assert not is_cancellation_annotation(names_to_anns["x"])
