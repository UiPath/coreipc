//! Error types: the public [`RpcError`] and the [`RemoteError`] mapped from an [`IpcError`].

use std::time::Duration;

use super::messages::IpcError;
use crate::wire::WireError;

/// A fault reported by the remote peer (the .NET service), mapped from the wire [`IpcError`].
///
/// Mirrors .NET's `RemoteException`: it carries the originating exception `type_name`, message,
/// stack trace, and a recursively-mapped inner error.
#[derive(Debug, Clone, PartialEq, thiserror::Error)]
#[error("{type_name}: {message}")]
pub struct RemoteError {
    pub message: String,
    pub stack_trace: Option<String>,
    pub type_name: String,
    pub inner: Option<Box<RemoteError>>,
}

impl RemoteError {
    /// True when the remote exception's type matches the given .NET full type name.
    ///
    /// Mirrors `RemoteException.Is<T>()` (`Type == typeof(T).FullName`).
    pub fn is_type(&self, dotnet_full_name: &str) -> bool {
        self.type_name == dotnet_full_name
    }
}

impl From<IpcError> for RemoteError {
    fn from(error: IpcError) -> Self {
        RemoteError {
            message: error.message,
            stack_trace: error.stack_trace,
            type_name: error.type_name,
            inner: error
                .inner_error
                .map(|inner| Box::new(RemoteError::from(*inner))),
        }
    }
}

impl From<RemoteError> for IpcError {
    fn from(error: RemoteError) -> Self {
        IpcError {
            message: error.message,
            stack_trace: error.stack_trace,
            type_name: error.type_name,
            inner_error: error.inner.map(|inner| Box::new(IpcError::from(*inner))),
        }
    }
}

/// The crate's primary error type for an RPC call.
#[derive(Debug, thiserror::Error)]
pub enum RpcError {
    /// The remote peer returned an error response.
    #[error("remote error: {0}")]
    Remote(#[from] RemoteError),
    /// Framing/deframing failed.
    #[error(transparent)]
    Wire(#[from] WireError),
    /// (De)serialization of the envelope or a payload failed.
    #[error("serialization error: {0}")]
    Serde(#[from] serde_json::Error),
    /// No response arrived within the per-call timeout.
    #[error("call timed out after {0:?}")]
    Timeout(Duration),
    /// The call was cancelled via its cancellation token.
    #[error("call cancelled")]
    Cancelled,
    /// The connection closed before the call completed.
    #[error("connection closed")]
    ConnectionClosed,
}
