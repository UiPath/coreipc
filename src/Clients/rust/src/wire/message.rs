//! Frame value types: the message kind discriminant and the raw frame.

use bytes::Bytes;

/// The frame `type` byte. Matches .NET `MessageType` (`Dtos.cs`).
///
/// `UploadRequest = 3` and `DownloadResponse = 4` exist in .NET but are out of scope for
/// this client, so they are intentionally not represented.
#[derive(Clone, Copy, PartialEq, Eq, Debug, Hash)]
#[repr(u8)]
pub enum MessageKind {
    Request = 0,
    Response = 1,
    Cancel = 2,
}

impl MessageKind {
    /// Map a wire `type` byte to a [`MessageKind`], or `None` for unsupported values.
    pub fn from_u8(byte: u8) -> Option<Self> {
        match byte {
            0 => Some(MessageKind::Request),
            1 => Some(MessageKind::Response),
            2 => Some(MessageKind::Cancel),
            _ => None,
        }
    }

    /// The wire `type` byte for this kind.
    pub fn as_u8(self) -> u8 {
        self as u8
    }
}

/// A single decoded/undecoded frame: the kind plus its raw UTF-8 JSON payload bytes.
#[derive(Clone, PartialEq, Eq, Debug)]
pub struct Frame {
    pub kind: MessageKind,
    pub data: Bytes,
}

impl Frame {
    pub fn new(kind: MessageKind, data: impl Into<Bytes>) -> Self {
        Self {
            kind,
            data: data.into(),
        }
    }
}
