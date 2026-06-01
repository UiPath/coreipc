//! Windows named-pipe transport (`\\.\pipe\<name>`).

use std::io;
use std::time::{Duration, Instant};

use async_trait::async_trait;
use tokio::net::windows::named_pipe::ClientOptions;
use tokio_util::sync::CancellationToken;

use super::{named_pipe_path, BoxStream, Transport};

/// `ERROR_PIPE_BUSY` — all pipe instances are busy; the documented retry condition.
const ERROR_PIPE_BUSY: i32 = 231;
/// Poll interval while waiting for a busy pipe to free up.
const BUSY_RETRY_INTERVAL: Duration = Duration::from_millis(50);

/// Connects to a CoreIpc service over a Windows named pipe.
pub struct NamedPipeTransport {
    name: String,
}

impl NamedPipeTransport {
    pub fn new(name: impl Into<String>) -> Self {
        Self { name: name.into() }
    }

    /// The resolved pipe path, e.g. `\\.\pipe\uipath-coreipc-test-pipe`.
    pub fn path(&self) -> String {
        named_pipe_path(&self.name)
    }
}

#[async_trait]
impl Transport for NamedPipeTransport {
    async fn connect(&self, timeout: Duration, ct: CancellationToken) -> io::Result<BoxStream> {
        let path = self.path();
        let deadline = Instant::now() + timeout;
        loop {
            match ClientOptions::new().open(&path) {
                Ok(client) => return Ok(Box::new(client)),
                Err(err) if err.raw_os_error() == Some(ERROR_PIPE_BUSY) => {
                    if Instant::now() >= deadline {
                        return Err(io::Error::new(
                            io::ErrorKind::TimedOut,
                            format!("timed out waiting for busy pipe {path}"),
                        ));
                    }
                    tokio::select! {
                        _ = ct.cancelled() => {
                            return Err(io::Error::new(io::ErrorKind::Interrupted, "connect cancelled"));
                        }
                        _ = tokio::time::sleep(BUSY_RETRY_INTERVAL) => {}
                    }
                }
                Err(err) => return Err(err),
            }
        }
    }
}
