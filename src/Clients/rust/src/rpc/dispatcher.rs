//! Routing of server-initiated requests (callbacks/events) to a handler.
//!
//! The channel is symmetric: the server also sends `Request` frames to the client. Those are
//! routed through a [`Dispatcher`], whose result is double-encoded back into a `Response`.

use async_trait::async_trait;
use tokio_util::sync::CancellationToken;

use super::error::RemoteError;

/// Handles one incoming server-initiated request.
///
/// `params` are the raw double-encoded argument strings (inner JSON). The return is the
/// double-encoded result `Data` (`None` for void), or a [`RemoteError`] to send back as an
/// error response.
#[async_trait]
pub trait Dispatcher: Send + Sync {
    async fn dispatch(
        &self,
        endpoint: &str,
        method: &str,
        params: Vec<String>,
        ct: CancellationToken,
    ) -> Result<Option<String>, RemoteError>;
}

/// A dispatcher with no registered handlers — rejects every incoming request.
pub struct NoDispatcher;

#[async_trait]
impl Dispatcher for NoDispatcher {
    async fn dispatch(
        &self,
        endpoint: &str,
        method: &str,
        _params: Vec<String>,
        _ct: CancellationToken,
    ) -> Result<Option<String>, RemoteError> {
        Err(RemoteError {
            message: format!("No callback handler registered for {endpoint}.{method}"),
            stack_trace: None,
            type_name: "System.InvalidOperationException".into(),
            inner: None,
        })
    }
}
