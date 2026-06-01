//! [`MessageCodec`]: a `tokio_util` [`Encoder`]/[`Decoder`] for the length-prefixed frame.
//!
//! The incremental [`Decoder`] contract handles chunked/partial reads for free — it is the
//! idiomatic replacement for the manual "read 1, then 4, then `length`" loop in the .NET and
//! JS implementations.

use bytes::{Buf, BufMut, BytesMut};
use tokio_util::codec::{Decoder, Encoder};

use super::message::{Frame, MessageKind};

/// Frame header size: one `type` byte + a little-endian `i32` length.
pub const HEADER_LEN: usize = 1 + std::mem::size_of::<i32>();

/// Errors produced while framing/deframing.
#[derive(Debug, thiserror::Error)]
pub enum WireError {
    #[error("io error: {0}")]
    Io(#[from] std::io::Error),
    #[error("unknown message kind: {0}")]
    UnknownKind(u8),
    #[error("message length {len} exceeds maximum {max}")]
    MessageTooLarge { len: usize, max: usize },
    #[error("negative message length: {0}")]
    NegativeLength(i32),
}

/// Encodes/decodes [`Frame`]s on a byte stream.
#[derive(Debug, Clone)]
pub struct MessageCodec {
    max_message_size: usize,
}

impl MessageCodec {
    /// A codec whose maximum message size matches the .NET default (`i32::MAX`).
    pub fn new() -> Self {
        Self {
            max_message_size: i32::MAX as usize,
        }
    }

    /// A codec with a custom maximum payload length (in bytes).
    pub fn with_max_message_size(max: usize) -> Self {
        Self {
            max_message_size: max,
        }
    }
}

impl Default for MessageCodec {
    fn default() -> Self {
        Self::new()
    }
}

impl Decoder for MessageCodec {
    type Item = Frame;
    type Error = WireError;

    fn decode(&mut self, src: &mut BytesMut) -> Result<Option<Frame>, WireError> {
        if src.len() < HEADER_LEN {
            // Not enough bytes for a header yet.
            return Ok(None);
        }

        // Peek the header without consuming, so a partial payload can be retried later.
        let kind_byte = src[0];
        let len_i32 = i32::from_le_bytes([src[1], src[2], src[3], src[4]]);
        if len_i32 < 0 {
            return Err(WireError::NegativeLength(len_i32));
        }
        let len = len_i32 as usize;
        if len > self.max_message_size {
            return Err(WireError::MessageTooLarge {
                len,
                max: self.max_message_size,
            });
        }

        if src.len() < HEADER_LEN + len {
            // Reserve the remainder so the read loop can fill it in one more pass.
            src.reserve(HEADER_LEN + len - src.len());
            return Ok(None);
        }

        // A full frame is buffered. Consume the header, then the payload.
        match MessageKind::from_u8(kind_byte) {
            Some(kind) => {
                src.advance(HEADER_LEN);
                let data = src.split_to(len).freeze();
                Ok(Some(Frame { kind, data }))
            }
            None => {
                // Consume the whole frame to stay byte-aligned, then surface the error.
                src.advance(HEADER_LEN + len);
                Err(WireError::UnknownKind(kind_byte))
            }
        }
    }
}

impl Encoder<Frame> for MessageCodec {
    type Error = WireError;

    fn encode(&mut self, item: Frame, dst: &mut BytesMut) -> Result<(), WireError> {
        let len = item.data.len();
        if len > self.max_message_size {
            return Err(WireError::MessageTooLarge {
                len,
                max: self.max_message_size,
            });
        }
        dst.reserve(HEADER_LEN + len);
        dst.put_u8(item.kind.as_u8());
        dst.put_i32_le(len as i32);
        dst.extend_from_slice(&item.data);
        Ok(())
    }
}
