"""uipath-ipc: Python IPC library compatible with UiPath CoreIpc wire protocol."""

from ._version import __version__

# Server
from .server.contract import ContractCollection, ContractSettings
from .server.ipc_server import IpcServer

# Client
from .client.ipc_client import IpcClient

# Transports
from .transport.tcp import TcpClientTransport, TcpServerTransport
from .transport.named_pipe import NamedPipeClientTransport, NamedPipeServerTransport

# Wire types
from .wire.dtos import Error, Request, Response

# Errors
from .errors import EndpointNotFoundException, RemoteException

# Cancellation
from .cancellation import CancellationToken
