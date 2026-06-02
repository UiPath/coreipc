//! The high-level client: typed `call` and server-initiated callback registration.

use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use async_trait::async_trait;
use serde::de::DeserializeOwned;
use tokio::io::{AsyncRead, AsyncWrite};
use tokio_util::sync::CancellationToken;

use crate::connect::{ConnectContext, ConnectStrategy, DefaultConnect};
use crate::rpc::dispatcher::Dispatcher;
use crate::rpc::error::{RemoteError, RpcError};
use crate::rpc::messages::WireRequest;
use crate::rpc::RpcChannel;
use crate::serde_conv::{decode_ret, EncodeArgs};
use crate::transport::{default_transport, Transport};

/// .NET's default request timeout is 40 seconds (`Connection`/`ServiceClient`).
pub const DEFAULT_TIMEOUT: Duration = Duration::from_secs(40);

/// Tunables for a [`Client`].
#[derive(Debug, Clone)]
pub struct ClientOptions {
    /// Timeout applied to a `call` when none is passed explicitly.
    pub default_timeout: Duration,
    /// Timeout applied while establishing the connection.
    pub connect_timeout: Duration,
}

impl Default for ClientOptions {
    fn default() -> Self {
        Self {
            default_timeout: DEFAULT_TIMEOUT,
            connect_timeout: DEFAULT_TIMEOUT,
        }
    }
}

/// Handles server-initiated requests for one endpoint (a client-side callback contract).
///
/// `params` are the raw double-encoded argument strings. Implementors decode them, run their
/// logic, and return the double-encoded result `Data` (`None` for void) — or a [`RemoteError`]
/// to send back as an error response.
#[async_trait]
pub trait IncomingHandler: Send + Sync {
    async fn invoke(
        &self,
        method: &str,
        params: Vec<String>,
        ct: CancellationToken,
    ) -> Result<Option<String>, RemoteError>;
}

/// Maps an endpoint name to its registered [`IncomingHandler`]. Implements [`Dispatcher`], so
/// it is what the channel routes incoming requests through. Uses interior mutability, so
/// handlers may be registered after the channel starts.
struct CallbackRegistry {
    handlers: Mutex<HashMap<String, Arc<dyn IncomingHandler>>>,
}

impl CallbackRegistry {
    fn new() -> Self {
        Self {
            handlers: Mutex::new(HashMap::new()),
        }
    }

    fn register(&self, endpoint: &str, handler: Arc<dyn IncomingHandler>) {
        self.handlers
            .lock()
            .unwrap()
            .insert(endpoint.to_string(), handler);
    }
}

#[async_trait]
impl Dispatcher for CallbackRegistry {
    async fn dispatch(
        &self,
        endpoint: &str,
        method: &str,
        params: Vec<String>,
        ct: CancellationToken,
    ) -> Result<Option<String>, RemoteError> {
        let handler = self.handlers.lock().unwrap().get(endpoint).cloned();
        match handler {
            Some(handler) => handler.invoke(method, params, ct).await,
            None => Err(RemoteError {
                message: format!("No callback handler registered for endpoint '{endpoint}'"),
                stack_trace: None,
                type_name: "System.InvalidOperationException".into(),
                inner: None,
            }),
        }
    }
}

/// A connected CoreIpc client over a single channel.
#[derive(Clone)]
pub struct Client {
    channel: RpcChannel,
    registry: Arc<CallbackRegistry>,
    default_timeout: Duration,
}

impl Client {
    /// Connect to a CoreIpc service by pipe name using the platform default transport and the
    /// default connect strategy (connect once, no application handshake).
    pub async fn connect(name: &str, options: ClientOptions) -> std::io::Result<Client> {
        let transport = default_transport(name);
        Client::connect_with(
            transport.as_ref(),
            &DefaultConnect,
            options,
            CancellationToken::new(),
        )
        .await
    }

    /// Connect using an explicit transport, strategy, and cancellation token.
    pub async fn connect_with(
        transport: &dyn Transport,
        strategy: &dyn ConnectStrategy,
        options: ClientOptions,
        ct: CancellationToken,
    ) -> std::io::Result<Client> {
        let ctx = ConnectContext {
            timeout: options.connect_timeout,
            ct,
        };
        let stream = strategy.connect(transport, &ctx).await?;
        Ok(Client::from_stream(stream, options))
    }

    /// Build a client driving the protocol over an already-connected stream.
    ///
    /// Transports (named pipe / UDS) produce the stream; this is the seam that keeps the
    /// client transport-agnostic and makes loopback testing trivial.
    pub fn from_stream<S>(stream: S, options: ClientOptions) -> Client
    where
        S: AsyncRead + AsyncWrite + Send + Unpin + 'static,
    {
        let registry = Arc::new(CallbackRegistry::new());
        let channel = RpcChannel::start(stream, registry.clone());
        Client {
            channel,
            registry,
            default_timeout: options.default_timeout,
        }
    }

    /// Register a handler for server-initiated callbacks on `endpoint`.
    ///
    /// Mirrors the JS `ipc.callback.forService('IArithmetic').is(handler)`.
    pub fn register<H>(&self, endpoint: &str, handler: H)
    where
        H: IncomingHandler + 'static,
    {
        self.registry.register(endpoint, Arc::new(handler));
    }

    /// Invoke `endpoint.method(args)` and decode the result as `R`.
    ///
    /// `args` is a tuple (e.g. `(2, 3)`); each element is double-encoded into `Parameters`.
    /// `timeout` defaults to [`ClientOptions::default_timeout`] when `None`.
    pub async fn call<A, R>(
        &self,
        endpoint: &str,
        method: &str,
        args: A,
        timeout: Option<Duration>,
        ct: CancellationToken,
    ) -> Result<R, RpcError>
    where
        A: EncodeArgs,
        R: DeserializeOwned,
    {
        let parameters = args.encode_args()?;
        let timeout = timeout.unwrap_or(self.default_timeout);
        let request = WireRequest {
            id: self.channel.next_request_id(),
            timeout_in_seconds: timeout.as_secs_f64(),
            endpoint: endpoint.to_string(),
            method_name: method.to_string(),
            parameters,
        };
        let response = self.channel.call_raw(request, timeout, ct).await?;
        Ok(decode_ret::<R>(response.data.as_deref())?)
    }

    /// Invoke `endpoint.method(args)` fire-and-forget: send the request frame without awaiting a
    /// response. Mirrors the reference client's FireAndForget dispatch — the server still sends a void
    /// Response, which is discarded as orphaned. Returns once the frame is queued.
    ///
    /// `timeout_in_seconds` is sent for the server's benefit only; the client never enforces it
    /// (there is no response to wait on).
    pub async fn notify<A>(&self, endpoint: &str, method: &str, args: A) -> Result<(), RpcError>
    where
        A: EncodeArgs,
    {
        let parameters = args.encode_args()?;
        let request = WireRequest {
            id: self.channel.next_request_id(),
            timeout_in_seconds: self.default_timeout.as_secs_f64(),
            endpoint: endpoint.to_string(),
            method_name: method.to_string(),
            parameters,
        };
        self.channel.send_oneway(request).await
    }

    /// Gracefully close the underlying channel.
    pub fn shutdown(&self) {
        self.channel.shutdown();
    }

    /// True once the channel has closed.
    pub fn is_closed(&self) -> bool {
        self.channel.is_closed()
    }
}
