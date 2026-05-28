# uipath-ipc

Python **client** for [UiPath.Ipc](https://github.com/UiPath/coreipc) — an interface-based RPC framework with .NET server and client, TypeScript client, and now Python client.

This package speaks the same wire protocol as the .NET package, so a Python client can talk to any UiPath.Ipc server.

## Status

- **Scope**: client only. Server, callbacks (bidirectional), and stream uploads/downloads are not included.
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
```

`__cause__` is set on the exception chain so Python tracebacks display the inner errors naturally.

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

## What's NOT in this client (yet)

- **Server side** — a Python server isn't planned for the initial port.
- **Callbacks** (bidirectional). The .NET client supports them; adding them to Python requires the client to host its own dispatcher. Park until needed.
- **Streams** (UploadRequest / DownloadResponse message types). Add on demand.
- **WebSocket transport**. Pending; will be an optional extra.

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
