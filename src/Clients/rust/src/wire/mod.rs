//! The wire framing layer: a transport-agnostic, length-prefixed frame codec.
//!
//! A frame is `[type: u8][length: i32 little-endian][data: `length` bytes]`. The codec
//! knows nothing about RPC semantics — it only moves `Frame { kind, data }` values. This
//! keeps the RPC/serde layers testable in isolation.

pub mod codec;
pub mod message;

pub use codec::{MessageCodec, WireError, HEADER_LEN};
pub use message::{Frame, MessageKind};
