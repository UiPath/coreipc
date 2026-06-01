//! Golden-fixture tests — offline serde parity against real .NET (Newtonsoft) output.
//!
//! The fixtures in `tests/fixtures/` are exact JSON payloads captured from the live
//! `UiPath.CoreIpc.NodeInterop` host (see `golden_capture.rs`). These tests need no host: they
//! prove our envelope/DTO deserialization matches genuine .NET serialization, and that our
//! re-serialization stays byte-stable.

use serde::{Deserialize, Serialize};
use uipath_coreipc::rpc::WireResponse;
use uipath_coreipc::serde_conv::decode_ret;

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Dto {
    bool_property: bool,
    int_property: i32,
    string_property: String,
}

#[test]
fn golden_multiply_simple_response() {
    let json = include_str!("fixtures/multiply_simple.response.json");
    let resp: WireResponse = serde_json::from_str(json).unwrap();
    assert_eq!(resp.request_id, "0");
    assert!(resp.error.is_none());
    // Double-encoded return decodes to the integer 6.
    assert_eq!(decode_ret::<i32>(resp.data.as_deref()).unwrap(), 6);
}

#[test]
fn golden_timeout_error_response() {
    let json = include_str!("fixtures/timeout.error.response.json");
    let resp: WireResponse = serde_json::from_str(json).unwrap();
    assert_eq!(resp.request_id, "1");
    assert!(resp.data.is_none());
    let error = resp.error.expect("error present");
    assert_eq!(error.type_name, "System.TimeoutException");
    assert_eq!(error.message, "The operation has timed out.");
    // .NET omits InnerError when null — our Option<Box<IpcError>> tolerates the absence.
    assert!(error.inner_error.is_none());
    assert!(error.stack_trace.is_some());
}

#[test]
fn golden_return_dto_response() {
    let json = include_str!("fixtures/return_dto.response.json");
    let resp: WireResponse = serde_json::from_str(json).unwrap();
    // Data is the double-encoded DTO; decode it and check PascalCase parity.
    let dto: Dto = decode_ret(resp.data.as_deref()).unwrap();
    assert_eq!(
        dto,
        Dto {
            bool_property: true,
            int_property: 42,
            string_property: "hi".into(),
        }
    );
    // Re-serialize the inner DTO and confirm it matches .NET's PascalCase form byte-for-byte.
    assert_eq!(
        serde_json::to_string(&dto).unwrap(),
        r#"{"BoolProperty":true,"IntProperty":42,"StringProperty":"hi"}"#
    );
}
