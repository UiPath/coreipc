//! Unix-domain-socket transport (macOS/Linux): `${TMPDIR:-/tmp}/CoreFxPipe_<name>`.
//!
//! .NET implements named pipes on Unix over a UDS at this path; the Rust client connects to it.

use std::io;
use std::time::Duration;

use async_trait::async_trait;
use tokio::net::UnixStream;
use tokio_util::sync::CancellationToken;

use super::{resolve_unix_path, BoxStream, Transport};

/// Connects to a CoreIpc service over a Unix domain socket.
pub struct UdsTransport {
    name: String,
}

impl UdsTransport {
    pub fn new(name: impl Into<String>) -> Self {
        Self { name: name.into() }
    }

    /// The resolved socket path, e.g. `/tmp/CoreFxPipe_uipath-coreipc-test-pipe`.
    pub fn path(&self) -> std::path::PathBuf {
        resolve_unix_path(&self.name)
    }
}

#[async_trait]
impl Transport for UdsTransport {
    async fn connect(&self, timeout: Duration, ct: CancellationToken) -> io::Result<BoxStream> {
        let path = self.path();
        tokio::select! {
            _ = ct.cancelled() => Err(io::Error::new(io::ErrorKind::Interrupted, "connect cancelled")),
            _ = tokio::time::sleep(timeout) => Err(io::Error::new(
                io::ErrorKind::TimedOut,
                format!("timed out connecting to {}", path.display()),
            )),
            res = UnixStream::connect(&path) => {
                let stream = res?;
                Ok(Box::new(stream))
            }
        }
    }
}
