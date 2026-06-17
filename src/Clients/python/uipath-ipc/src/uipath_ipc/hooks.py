"""Connection/call hooks — the Python analog of .NET's `BeforeConnect` and
`BeforeCall` (BeforeOutgoingCall / BeforeIncomingCall).

A hook may be sync or ``async``; the framework awaits it if it returns an
awaitable. If a hook raises, the connect/call it guards fails — so hooks can
both observe (logging, metrics) and gate (inject context, refuse a call).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Awaitable, Callable, Union


@dataclass(frozen=True, slots=True)
class CallInfo:
    """Describes a single RPC call, passed to a `BeforeCallHandler`.

    Mirrors .NET's `CallInfo` (method + arguments), with the wire endpoint
    (the contract's name) added so one handler can branch by interface.
    """

    endpoint: str
    method_name: str
    arguments: tuple[object, ...]
    #: True when this call established a fresh connection — client side only;
    #: always False for incoming/server calls. Mirrors .NET CallInfo.NewConnection.
    new_connection: bool = False


#: Runs before each connection attempt (client side). Sync or async.
BeforeConnectHandler = Callable[[], Union[Awaitable[None], None]]

#: Runs before each call — outgoing on the client, incoming on the server.
#: Sync or async. Receives the `CallInfo`; raising aborts the call.
BeforeCallHandler = Callable[[CallInfo], Union[Awaitable[None], None]]
