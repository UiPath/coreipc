//! Transport abstraction: the only place `#[cfg(windows)]`/`#[cfg(unix)]` appears.
//!
//! Every transport yields a [`BoxStream`] — a boxed `AsyncRead + AsyncWrite` — so the codec,
//! RPC, and client layers are entirely platform-agnostic and a future WebSocket transport can
//! be added as a third impl with no upstream changes.

use std::io;
use std::time::Duration;

use async_trait::async_trait;
use tokio::io::{AsyncRead, AsyncWrite};
use tokio_util::sync::CancellationToken;

#[cfg(windows)]
pub mod named_pipe;
#[cfg(unix)]
pub mod uds;

/// Marker for a bidirectional async byte stream usable by [`crate::rpc::RpcChannel`].
pub trait DuplexStream: AsyncRead + AsyncWrite + Send + Unpin {}
impl<T: AsyncRead + AsyncWrite + Send + Unpin> DuplexStream for T {}

/// An owned, type-erased connected stream.
pub type BoxStream = Box<dyn DuplexStream + 'static>;

/// Resolves a pipe name to a connected [`BoxStream`].
#[async_trait]
pub trait Transport: Send + Sync {
    async fn connect(&self, timeout: Duration, ct: CancellationToken) -> io::Result<BoxStream>;
}

/// The Windows named-pipe path for a pipe name: `\\.\pipe\<name>`.
///
/// Pure (no I/O), so it is unit-testable on any host.
pub fn named_pipe_path(name: &str) -> String {
    format!(r"\\.\pipe\{name}")
}

/// The Unix-domain-socket path .NET uses for a CoreIpc pipe name.
///
/// An absolute name (leading `/`) is used verbatim; otherwise the path is
/// `${TMPDIR:-/tmp}/CoreFxPipe_<name>`. Absolute detection uses the Unix convention
/// (leading `/`) rather than `Path::is_absolute`, so the result is identical on every host —
/// which keeps it unit-testable on Windows too. Pure (no I/O).
pub fn resolve_unix_path(name: &str) -> std::path::PathBuf {
    if name.starts_with('/') {
        return std::path::PathBuf::from(name);
    }
    let temp = std::env::var("TMPDIR").unwrap_or_else(|_| "/tmp".to_string());
    let temp = temp.trim_end_matches('/');
    std::path::PathBuf::from(format!("{temp}/CoreFxPipe_{name}"))
}

/// The default transport for the current platform: named pipe on Windows, UDS on Unix.
pub fn default_transport(name: &str) -> std::sync::Arc<dyn Transport> {
    #[cfg(windows)]
    {
        std::sync::Arc::new(named_pipe::NamedPipeTransport::new(name))
    }
    #[cfg(unix)]
    {
        std::sync::Arc::new(uds::UdsTransport::new(name))
    }
}
