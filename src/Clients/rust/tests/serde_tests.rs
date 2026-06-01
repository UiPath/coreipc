//! Phase B — envelope serde unit tests.
//!
//! Locks the exact JSON shape of the wire envelopes (PascalCase, null omission), the
//! double-encoding of `Parameters`/`Data`, the `IpcError` → `RemoteError` mapping, and the
//! shipped-DTO serialization convention (PascalCase) against the .NET implementation.

use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use uipath_coreipc::rpc::{IpcError, RemoteError, WireCancellation, WireRequest, WireResponse};
use uipath_coreipc::serde_conv::{decode_ret, encode_arg, EncodeArgs};

#[test]
fn request_serializes_to_exact_pascalcase_json() {
    let req = WireRequest {
        id: "0".into(),
        timeout_in_seconds: 40.0,
        endpoint: "IAlgebra".into(),
        method_name: "MultiplySimple".into(),
        parameters: vec!["2".into(), "3".into()],
    };
    // Struct field order is the serialization order — assert the literal string.
    let s = serde_json::to_string(&req).unwrap();
    assert_eq!(
        s,
        r#"{"Id":"0","TimeoutInSeconds":40.0,"Endpoint":"IAlgebra","MethodName":"MultiplySimple","Parameters":["2","3"]}"#
    );
    // No ObjectParameters key leaks onto the wire.
    let v: Value = serde_json::from_str(&s).unwrap();
    assert!(v.get("ObjectParameters").is_none());
    // Parameters elements are JSON *strings* (double-encoded).
    assert_eq!(v["Parameters"][0], json!("2"));
}

#[test]
fn request_round_trips() {
    let req = WireRequest {
        id: "7".into(),
        timeout_in_seconds: 1.5,
        endpoint: "ICalculus".into(),
        method_name: "Ping".into(),
        parameters: vec![],
    };
    let s = serde_json::to_string(&req).unwrap();
    let back: WireRequest = serde_json::from_str(&s).unwrap();
    assert_eq!(req, back);
}

#[test]
fn response_omits_null_data_and_error() {
    let resp = WireResponse {
        request_id: "0".into(),
        data: None,
        error: None,
    };
    assert_eq!(
        serde_json::to_string(&resp).unwrap(),
        r#"{"RequestId":"0"}"#
    );

    let resp = WireResponse {
        request_id: "0".into(),
        data: Some("6".into()),
        error: None,
    };
    assert_eq!(
        serde_json::to_string(&resp).unwrap(),
        r#"{"RequestId":"0","Data":"6"}"#
    );
}

#[test]
fn response_deserializes_data_and_error_forms() {
    let ok: WireResponse = serde_json::from_str(r#"{"RequestId":"0","Data":"6"}"#).unwrap();
    assert_eq!(ok.request_id, "0");
    assert_eq!(ok.data.as_deref(), Some("6"));
    assert!(ok.error.is_none());

    let err: WireResponse = serde_json::from_str(
        r#"{"RequestId":"1","Error":{"Message":"boom","StackTrace":"at X","Type":"System.Exception","InnerError":null}}"#,
    )
    .unwrap();
    assert!(err.data.is_none());
    let e = err.error.unwrap();
    assert_eq!(e.message, "boom");
    assert_eq!(e.type_name, "System.Exception");
    assert!(e.inner_error.is_none());

    // ObjectData and unknown fields are tolerated (ignored) on deserialize.
    let ok2: WireResponse =
        serde_json::from_str(r#"{"RequestId":"2","Data":"true","ObjectData":null,"Extra":1}"#)
            .unwrap();
    assert_eq!(ok2.data.as_deref(), Some("true"));
}

#[test]
fn cancellation_round_trips() {
    let c = WireCancellation {
        request_id: "3".into(),
    };
    assert_eq!(serde_json::to_string(&c).unwrap(), r#"{"RequestId":"3"}"#);
    let back: WireCancellation = serde_json::from_str(r#"{"RequestId":"3"}"#).unwrap();
    assert_eq!(c, back);
}

#[test]
fn ipc_error_nests_and_maps_to_remote_error() {
    let nested = r#"{
        "Message":"outer","StackTrace":"o","Type":"System.AggregateException",
        "InnerError":{"Message":"inner","StackTrace":"i","Type":"System.IO.IOException","InnerError":null}
    }"#;
    let err: IpcError = serde_json::from_str(nested).unwrap();
    let remote = RemoteError::from(err);
    assert_eq!(remote.type_name, "System.AggregateException");
    assert_eq!(remote.message, "outer");
    let inner = remote.inner.as_ref().expect("inner mapped");
    assert!(inner.is_type("System.IO.IOException"));
    assert_eq!(inner.message, "inner");
    assert!(inner.inner.is_none());
    // Display formats as "Type: message".
    assert_eq!(remote.to_string(), "System.AggregateException: outer");
}

#[test]
fn stacktrace_optional_on_error() {
    // .NET may omit StackTrace (NullValueHandling.Ignore); we must still parse.
    let err: IpcError =
        serde_json::from_str(r#"{"Message":"m","Type":"System.TimeoutException"}"#).unwrap();
    assert!(err.stack_trace.is_none());
    assert!(RemoteError::from(err).is_type("System.TimeoutException"));
}

#[test]
fn double_encode_args() {
    assert_eq!(encode_arg(&2).unwrap(), "2");
    assert_eq!(encode_arg(&"hi").unwrap(), r#""hi""#);
    assert_eq!(encode_arg(&true).unwrap(), "true");

    assert_eq!(().encode_args().unwrap(), Vec::<String>::new());
    assert_eq!((2,).encode_args().unwrap(), vec!["2"]);
    assert_eq!((2, 3).encode_args().unwrap(), vec!["2", "3"]);
    assert_eq!(
        (1, "x", true).encode_args().unwrap(),
        vec!["1", r#""x""#, "true"]
    );

    // Vec<Value> escape hatch.
    let dynamic = vec![json!(5), json!("y")];
    assert_eq!(dynamic.encode_args().unwrap(), vec!["5", r#""y""#]);
}

#[test]
fn decode_return_values() {
    assert_eq!(decode_ret::<i32>(Some("6")).unwrap(), 6);
    assert_eq!(decode_ret::<String>(Some(r#""Pong""#)).unwrap(), "Pong");
    assert!(decode_ret::<bool>(Some("true")).unwrap());
    // void / null returns.
    decode_ret::<()>(None).unwrap();
    decode_ret::<()>(Some("null")).unwrap();
    decode_ret::<()>(Some("")).unwrap();
    assert_eq!(decode_ret::<Option<i32>>(Some("null")).unwrap(), None);
}

#[test]
fn dto_uses_pascalcase_convention() {
    // Mirrors NodeInterop's `Dto { bool BoolProperty; int IntProperty; string StringProperty }`.
    #[derive(Debug, PartialEq, Serialize, Deserialize)]
    #[serde(rename_all = "PascalCase")]
    struct Dto {
        bool_property: bool,
        int_property: i32,
        string_property: String,
    }

    let dto = Dto {
        bool_property: true,
        int_property: 42,
        string_property: "hi".into(),
    };
    let s = serde_json::to_string(&dto).unwrap();
    assert_eq!(
        s,
        r#"{"BoolProperty":true,"IntProperty":42,"StringProperty":"hi"}"#
    );
    let back: Dto = serde_json::from_str(&s).unwrap();
    assert_eq!(dto, back);

    // Round-trip through double-encoding (how a DTO arg actually travels).
    let encoded = encode_arg(&dto).unwrap();
    let decoded: Dto = decode_ret(Some(&encoded)).unwrap();
    assert_eq!(dto, decoded);
}
