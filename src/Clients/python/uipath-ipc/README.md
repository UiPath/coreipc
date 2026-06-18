# uipath-ipc

Python **client and server** for [UiPath.Ipc](https://github.com/UiPath/coreipc) — an interface-based RPC framework with .NET server and client, TypeScript client, and now a Python client and server.

This package speaks the same wire protocol as the .NET package, so a Python client can talk to any UiPath.Ipc server (and a Python `IpcServer` can host services for any client).

## Status

- **Scope**: client (`IpcClient`) and server (`IpcServer`), with bidirectional callbacks. Stream uploads/downloads are not implemented.
- **Transports**: Named Pipe, TCP. (WebSocket is on the roadmap.)
- **Python**: 3.10+.

## Install

```bash
pip install uipath-ipc
```

## Quick start

### 1. Define a contract

The contract is a Python ABC whose method names exactly match the .NET interface methods. Each method must be `async def`.

```python
from abc import ABC, abstractmethod


class IComputingService(ABC):
    @abstractmethod
    async def AddFloats(self, x: float, y: float) -> float: ...

    @abstractmethod
    async def Wait(self, duration: float) -> bool: ...
```

### 2. Create a client and call methods

```python
import asyncio
from uipath_ipc import IpcClient, NamedPipeClientTransport


async def main() -> None:
    transport = NamedPipeClientTransport(pipe_name="test")
    async with IpcClient(transport) as client:
        svc = client.get_proxy(IComputingService)

        result = await svc.AddFloats(1.5, 2.5)
        print(result)  # 4.0


asyncio.run(main())
```

The proxy returned by `get_proxy(IComputingService)` looks like an instance of the contract to your editor and type checker — call its methods normally.

## Features

### Cancellation

Cancellation in Python is **task-based**, not token-based. You cancel by cancelling the task that's awaiting:

```python
task = asyncio.create_task(svc.Wait(10.0))
await asyncio.sleep(0.1)
task.cancel()              # CancelledError propagates up through await
```

When the proxy observes `CancelledError`, it sends a `CancellationRequest` frame to the server (matching the in-flight request id) before re-raising.

### Timeouts

Configure a per-client default:

```python
async with IpcClient(transport, request_timeout=5.0) as client:
    ...
```

Or override per-call with `asyncio.timeout` (3.11+) / `asyncio.wait_for`:

```python
async with asyncio.timeout(1.0):
    await svc.Wait(10.0)   # raises TimeoutError after 1s
```

In both cases the server is notified via a `CancellationRequest`.

For a single call, pass a `Message` argument carrying the timeout — it overrides the client default for that call only:

```python
from uipath_ipc import Message, INFINITE_REQUEST_TIMEOUT

await svc.Install(pkg, Message(request_timeout=1200))                       # 20-minute call
await svc.SignIn(creds, Message(request_timeout=INFINITE_REQUEST_TIMEOUT))  # no deadline
```

`INFINITE_REQUEST_TIMEOUT` is the .NET `Timeout.InfiniteTimeSpan` rendition: no client-side deadline, and the server reads it as "no timeout". A `request_timeout` of `0` means "use the server's default" (it does not override the client default).

### Exception propagation

Server-side exceptions surface as `RemoteException`:

```python
from uipath_ipc import RemoteException

try:
    await svc.DivideByZero()
except RemoteException as ex:
    print(ex.message)       # "Attempted to divide by zero."
    print(ex.type_name)     # "System.DivideByZeroException"
    print(ex.stack_trace)   # the .NET stack
    print(ex.inner)         # inner RemoteException (chain), or None
    ex.is_remote_type("System.DivideByZeroException")   # True — .NET Is<T>() analog
```

`__cause__` is set on the exception chain so Python tracebacks display the inner errors naturally.

### Callbacks (server → client)

The server can invoke methods on objects that *the client* hosts. Define the callback contract, pass an instance to `IpcClient(callbacks={...})`, and the proxy on the server side can call into your Python object:

```python
from abc import ABC, abstractmethod


class IClientCallback(ABC):
    @abstractmethod
    async def EchoToClient(self, value: str) -> str: ...


class EchoHandler:
    async def EchoToClient(self, value: str) -> str:
        return f"echoed: {value}"


async with IpcClient(transport, callbacks={IClientCallback: EchoHandler()}) as client:
    tester = client.get_proxy(ICallbackTester)
    print(await tester.TriggerEcho("hi"))   # "echoed: hi"
```

Callback methods must be `async def` (like service handlers): a synchronous handler runs inline on the event loop, blocking the whole connection for its duration and escaping the request timeout. Exceptions raised inside the handler are wired back to the server as `RemoteException`. Server-initiated cancellations cancel the in-flight handler task.

### Hooks

Two optional hooks let you observe or gate the client (the analog of .NET's `BeforeConnect` / `BeforeOutgoingCall`). Each may be sync or `async`; **raising in a hook aborts** the connect/call.

```python
from uipath_ipc import CallInfo

async def launch_server() -> None:
    ...  # e.g. lazily start the server before the first connect (self-healing)

def log_call(ci: CallInfo) -> None:
    print(ci.endpoint, ci.method_name, ci.arguments, ci.new_connection)

async with IpcClient(transport, before_connect=launch_server, before_call=log_call) as client:
    ...
```

`before_connect` runs before each (re)connect; `before_call` runs before each outgoing call with a `CallInfo` (`endpoint`, `method_name`, `arguments`, and `new_connection` — `True` only on the call that opened the connection).

### Custom serialization (advanced)

The proxy materializes results into a contract's declared return type via reflection — `bytes`, `UUID`, `datetime`, `Decimal`, enums, and dataclasses all round-trip (see [Features](#features) above). If you need to (de)serialize values yourself, the same primitives are exported as `from_wire(value, hint)` / `to_wire(value)`. The contract vocabulary is intentionally narrow — plain JSON values and dataclasses — so the IPC layer stays decoupled from any modeling framework (pydantic, ORM entities, …); map IPC DTOs to your own validated/domain types at your boundary if you need them.

### Auto-reconnect

The client opens a connection lazily on the first call and reuses it. If the underlying stream drops (server restart, network blip), the **next** call transparently re-dials via the transport. The proxy instance remains valid across reconnects.

In-flight calls when the drop happens propagate the underlying error rather than silently retrying — that's the caller's policy choice.

## Transports

```python
from uipath_ipc import NamedPipeClientTransport, TcpClientTransport

NamedPipeClientTransport(pipe_name="test")                  # local
NamedPipeClientTransport(pipe_name="test", server_name="REMOTE")  # remote (Windows)
TcpClientTransport(host="127.0.0.1", port=5050)
```

Custom transports are easy: subclass `ClientTransport` and implement `connect()`.

## What's NOT implemented (yet)

- **Streams** (UploadRequest / DownloadResponse message types). Add on demand.
- **WebSocket transport**. Pending; will be an optional extra.
- **Configurable max message size** — the 2 MB cap (matching .NET's default) is fixed; .NET's `MaxReceivedMessageSizeInMegabytes` knob isn't exposed yet.

## Development

```bash
# Clone, set up env
py -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e ".[dev]"

# Run tests
pytest

# Build wheel + sdist
pip install build
python -m build
```

## Wire protocol cheat sheet

- **Frame**: 5-byte header + UTF-8 JSON payload.
- **Header**: `[MessageType: uint8][PayloadLength: int32 LE]`.
- **Message types**: `Request=0`, `Response=1`, `CancellationRequest=2`, `UploadRequest=3`, `DownloadResponse=4`.
- **Request.Parameters** is a list of *individually JSON-encoded* strings — `[\"1.5\", \"\\\"hi\\\"\"]`, not `[1.5, \"hi\"]`.

## License

MIT.
