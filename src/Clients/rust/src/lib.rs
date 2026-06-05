//! Rust client for the UiPath CoreIpc named-pipe / Unix-domain-socket RPC protocol.
//!
//! The crate is wire-compatible with genuine .NET CoreIpc as it exists at the pinned
//! protocol commit. See `DESIGN.md` for the authoritative protocol description.
//!
//! Layering (bottom-up):
//! - [`wire`] — length-prefixed frame codec (`[type:u8][length:i32 LE][data]`).
//! - `rpc` — JSON envelope structs, error mapping, the bidirectional channel.
//! - `transport` / `connect` — named-pipe (Windows) and UDS (Unix) transports.
//! - `client` — the high-level typed `call`/callback API.

pub mod client;
pub mod connect;
pub mod rpc;
pub mod serde_conv;
pub mod transport;
pub mod wire;

#[doc(no_inline)]
pub use client::{CallTimeout, Client, ClientOptions, IncomingHandler};
#[doc(no_inline)]
pub use connect::{ConnectContext, ConnectStrategy, DefaultConnect};
#[doc(no_inline)]
pub use rpc::{Dispatcher, RemoteError, RpcChannel, RpcError};
#[doc(no_inline)]
pub use tokio_util::sync::CancellationToken;
#[doc(no_inline)]
pub use transport::{BoxStream, Transport};
