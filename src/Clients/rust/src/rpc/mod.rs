//! The RPC layer: JSON envelope structs, error mapping, and (later) the bidirectional channel.

pub mod channel;
pub mod dispatcher;
pub mod error;
pub mod messages;

pub use channel::RpcChannel;
pub use dispatcher::{Dispatcher, NoDispatcher};
pub use error::{RemoteError, RpcError};
pub use messages::{IpcError, WireCancellation, WireRequest, WireResponse};
