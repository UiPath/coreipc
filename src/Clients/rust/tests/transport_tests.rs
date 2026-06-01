//! Phase E — transport path resolution + connect strategy.
//!
//! Path resolution is pure, so it is tested on every host. The platform-specific connect
//! impls are exercised by the interop suite; here the strategy is tested against a mock
//! transport (no real socket).

use std::io;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::Arc;
use std::time::Duration;

use async_trait::async_trait;
use tokio::io::duplex;
use uipath_coreipc::connect::{ConnectContext, ConnectStrategy, DefaultConnect};
use uipath_coreipc::transport::{named_pipe_path, resolve_unix_path, BoxStream, Transport};
use uipath_coreipc::CancellationToken;

#[test]
fn windows_named_pipe_path() {
    assert_eq!(named_pipe_path("foo"), r"\\.\pipe\foo");
    assert_eq!(
        named_pipe_path("uipath-coreipc-test-pipe"),
        r"\\.\pipe\uipath-coreipc-test-pipe"
    );
}

#[test]
fn unix_relative_name_uses_corefxpipe_in_tmp() {
    // Default temp dir (no TMPDIR). We can't safely mutate process env in parallel tests, so
    // assert the shape rather than depend on TMPDIR being unset.
    let p = resolve_unix_path("mypipe");
    let s = p.to_string_lossy();
    assert!(s.ends_with("/CoreFxPipe_mypipe"), "got {s}");
    assert!(s.contains("CoreFxPipe_"));
}

#[test]
fn unix_absolute_name_passes_through() {
    let p = resolve_unix_path("/var/run/CoreFxPipe_x");
    assert_eq!(p.to_string_lossy(), "/var/run/CoreFxPipe_x");
}

#[test]
fn unix_respects_tmpdir_env() {
    // Isolated env var read; set then resolve within this single-threaded test body.
    std::env::set_var("TMPDIR", "/custom/tmp/");
    let p = resolve_unix_path("abc");
    std::env::remove_var("TMPDIR");
    assert_eq!(p.to_string_lossy(), "/custom/tmp/CoreFxPipe_abc");
}

/// A transport that hands back one end of an in-memory duplex, failing the first
/// `fail_times` attempts with `ConnectionRefused` to exercise retry strategies.
struct MockTransport {
    attempts: AtomicUsize,
    fail_times: usize,
}

impl MockTransport {
    fn new(fail_times: usize) -> Self {
        Self {
            attempts: AtomicUsize::new(0),
            fail_times,
        }
    }
    fn attempts(&self) -> usize {
        self.attempts.load(Ordering::SeqCst)
    }
}

#[async_trait]
impl Transport for MockTransport {
    async fn connect(&self, _timeout: Duration, _ct: CancellationToken) -> io::Result<BoxStream> {
        let n = self.attempts.fetch_add(1, Ordering::SeqCst);
        if n < self.fail_times {
            return Err(io::Error::new(io::ErrorKind::ConnectionRefused, "refused"));
        }
        let (stream, _peer) = duplex(64);
        // Keep the peer end alive for the duration of the process so the stream stays open.
        std::mem::forget(_peer);
        Ok(Box::new(stream))
    }
}

#[tokio::test]
async fn default_connect_calls_transport_once() {
    let transport = MockTransport::new(0);
    let ctx = ConnectContext {
        timeout: Duration::from_secs(1),
        ct: CancellationToken::new(),
    };
    let stream = DefaultConnect.connect(&transport, &ctx).await;
    assert!(stream.is_ok());
    assert_eq!(transport.attempts(), 1);
}

/// A custom strategy that retries up to `max` times — demonstrates the `ConnectHelper`
/// extensibility hook.
struct RetryConnect {
    max: usize,
}

#[async_trait]
impl ConnectStrategy for RetryConnect {
    async fn connect(
        &self,
        transport: &dyn Transport,
        ctx: &ConnectContext,
    ) -> io::Result<BoxStream> {
        let mut last = io::Error::other("never attempted");
        for _ in 0..self.max {
            match transport.connect(ctx.timeout, ctx.ct.clone()).await {
                Ok(stream) => return Ok(stream),
                Err(err) => last = err,
            }
        }
        Err(last)
    }
}

#[tokio::test]
async fn custom_strategy_retries_until_success() {
    let transport = Arc::new(MockTransport::new(2)); // fail twice, succeed on the third
    let ctx = ConnectContext {
        timeout: Duration::from_secs(1),
        ct: CancellationToken::new(),
    };
    let strategy = RetryConnect { max: 5 };
    let stream = strategy.connect(transport.as_ref(), &ctx).await;
    assert!(stream.is_ok());
    assert_eq!(transport.attempts(), 3);
}

#[tokio::test]
async fn custom_strategy_gives_up_after_max() {
    let transport = Arc::new(MockTransport::new(10));
    let ctx = ConnectContext {
        timeout: Duration::from_secs(1),
        ct: CancellationToken::new(),
    };
    let strategy = RetryConnect { max: 3 };
    match strategy.connect(transport.as_ref(), &ctx).await {
        Err(err) => assert_eq!(err.kind(), io::ErrorKind::ConnectionRefused),
        Ok(_) => panic!("expected the strategy to give up"),
    }
    assert_eq!(transport.attempts(), 3);
}
