//! Wire envelope structs — the JSON messages exchanged over a frame's payload.
//!
//! Field names are pinned with explicit `#[serde(rename = "...")]` to the exact PascalCase the
//! .NET records use (`Dtos.cs`). `Option` fields use `skip_serializing_if = "Option::is_none"`
//! to reproduce Newtonsoft's `NullValueHandling.Ignore` (null keys are omitted on the wire).
//!
//! .NET's `Request`/`Response` records also carry `ObjectParameters`/`ObjectData` (the binary
//! "object args" path). This client only uses the string (double-encoded JSON) path, so those
//! fields are intentionally absent here: we never emit them, and we ignore them when present.

use serde::{Deserialize, Serialize};

use crate::wire::{Frame, MessageKind};

/// A `Request` frame payload (frame type 0).
///
/// ```json
/// { "Id":"0", "TimeoutInSeconds":40.0, "Endpoint":"IAlgebra",
///   "MethodName":"MultiplySimple", "Parameters":["2","3"] }
/// ```
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WireRequest {
    #[serde(rename = "Id", default)]
    pub id: String,
    #[serde(rename = "TimeoutInSeconds", default)]
    pub timeout_in_seconds: f64,
    #[serde(rename = "Endpoint", default)]
    pub endpoint: String,
    #[serde(rename = "MethodName", default)]
    pub method_name: String,
    /// Each element is one double-encoded argument (JSON-within-JSON).
    ///
    /// `#[serde(default)]`: .NET omits default-valued members (`DefaultValueHandling`), so an
    /// incoming (server-initiated) request may legitimately omit `Parameters`/`TimeoutInSeconds`.
    #[serde(rename = "Parameters", default)]
    pub parameters: Vec<String>,
}

impl WireRequest {
    /// Serialize into a `Request` frame.
    pub fn to_frame(&self) -> Result<Frame, serde_json::Error> {
        Ok(Frame::new(MessageKind::Request, serde_json::to_vec(self)?))
    }
}

/// A `Response` frame payload (frame type 1).
///
/// `Data` is the double-encoded return value (or absent for void/null). `Error` is present
/// only on failure. Both are omitted from the wire when `None`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WireResponse {
    #[serde(rename = "RequestId")]
    pub request_id: String,
    #[serde(rename = "Data", default, skip_serializing_if = "Option::is_none")]
    pub data: Option<String>,
    #[serde(rename = "Error", default, skip_serializing_if = "Option::is_none")]
    pub error: Option<IpcError>,
}

impl WireResponse {
    /// Serialize into a `Response` frame.
    pub fn to_frame(&self) -> Result<Frame, serde_json::Error> {
        Ok(Frame::new(MessageKind::Response, serde_json::to_vec(self)?))
    }
}

/// A `CancellationRequest` frame payload (frame type 2).
///
/// ```json
/// { "RequestId":"0" }
/// ```
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WireCancellation {
    #[serde(rename = "RequestId")]
    pub request_id: String,
}

impl WireCancellation {
    /// Serialize into a `Cancel` frame.
    pub fn to_frame(&self) -> Result<Frame, serde_json::Error> {
        Ok(Frame::new(MessageKind::Cancel, serde_json::to_vec(self)?))
    }
}

/// The error object nested in [`WireResponse::error`] — .NET's `Error` record.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct IpcError {
    #[serde(rename = "Message")]
    pub message: String,
    #[serde(
        rename = "StackTrace",
        default,
        skip_serializing_if = "Option::is_none"
    )]
    pub stack_trace: Option<String>,
    /// The .NET exception type's full name, e.g. `System.TimeoutException`.
    #[serde(rename = "Type")]
    pub type_name: String,
    #[serde(
        rename = "InnerError",
        default,
        skip_serializing_if = "Option::is_none"
    )]
    pub inner_error: Option<Box<IpcError>>,
}
