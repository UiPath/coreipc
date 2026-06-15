"""uipath-ipc — Python client and server for UiPath.Ipc."""

from .client import IpcClient, IpcConnection
from .errors import EndpointNotFoundError, MethodNotFoundError, RemoteException
from .hooks import BeforeCallHandler, BeforeConnectHandler, CallInfo
from .message import INFINITE_REQUEST_TIMEOUT, IClient, Message
from .server import IpcServer
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
    "from_wire",
    "to_wire",
]
