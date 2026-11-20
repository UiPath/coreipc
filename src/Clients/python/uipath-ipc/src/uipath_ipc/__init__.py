"""uipath-ipc — Python client and server for UiPath.Ipc."""

from .client import IpcClient, IpcConnection
from .errors import EndpointNotFoundError, MethodNotFoundError, RemoteException
from .hooks import BeforeCallHandler, BeforeConnectHandler, CallInfo
from .markers import ipc_cancellable
from .message import INFINITE_REQUEST_TIMEOUT, IClient, Message
from .server import IpcServer
from .trace_context import (
    TelemetryPropagationPolicy,
    TraceContext,
    incoming_trace_context,
    use_trace_context,
)
from .transport import (
    ClientTransport,
    NamedPipeClientTransport,
    NamedPipeServerTransport,
    ServerTransport,
    TcpClientTransport,
    TcpServerTransport,
)
from .wire import from_wire, to_wire

__all__ = [
    "BeforeCallHandler",
    "BeforeConnectHandler",
    "CallInfo",
    "ClientTransport",
    "EndpointNotFoundError",
    "IClient",
    "INFINITE_REQUEST_TIMEOUT",
    "MethodNotFoundError",
    "IpcClient",
    "IpcConnection",
    "IpcServer",
    "Message",
    "NamedPipeClientTransport",
    "NamedPipeServerTransport",
    "RemoteException",
    "ServerTransport",
    "TcpClientTransport",
    "TcpServerTransport",
    "TelemetryPropagationPolicy",
    "TraceContext",
    "from_wire",
    "incoming_trace_context",
    "ipc_cancellable",
    "to_wire",
    "use_trace_context",
]
