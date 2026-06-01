//! The pluggable connect strategy — mirrors .NET/JS `ConnectHelper`.
//!
//! `ConnectHelper` is an extensibility hook: the default just connects the socket once, but a
//! custom strategy can retry, launch the server first, etc. Here it is an injected async
//! strategy over a [`Transport`]; the default is [`DefaultConnect`].

use std::io;
use std::time::Duration;

use async_trait::async_trait;
use tokio_util::sync::CancellationToken;

use crate::transport::{BoxStream, Transport};

/// Inputs available to a connect strategy.
#[derive(Clone)]
pub struct ConnectContext {
    pub timeout: Duration,
    pub ct: CancellationToken,
}

/// A strategy for establishing the connection over a [`Transport`].
#[async_trait]
pub trait ConnectStrategy: Send + Sync {
    async fn connect(
        &self,
        transport: &dyn Transport,
        ctx: &ConnectContext,
    ) -> io::Result<BoxStream>;
}

/// The default strategy: connect exactly once (`defaultConnectHelper`).
pub struct DefaultConnect;

#[async_trait]
impl ConnectStrategy for DefaultConnect {
    async fn connect(
        &self,
        transport: &dyn Transport,
        ctx: &ConnectContext,
    ) -> io::Result<BoxStream> {
        transport.connect(ctx.timeout, ctx.ct.clone()).await
    }
}
