//! Phase C — RPC channel tests over an in-memory loopback.
//!
//! A `tokio::io::duplex` pair simulates the socket: one end is driven by [`RpcChannel`], the
//! other by a hand-written "peer" (`Framed<_, MessageCodec>`) that reads requests and writes
//! responses. No .NET host involved — this proves correlation, concurrency, timeout,
//! cancellation, disconnect, and the bidirectional callback path hermetically.

use std::sync::Arc;
use std::time::Duration;

use async_trait::async_trait;
use futures_util::{SinkExt, StreamExt};
use tokio::io::{duplex, DuplexStream};
use tokio_util::codec::Framed;

use uipath_coreipc::rpc::{Dispatcher, NoDispatcher, RemoteError, RpcChannel};
use uipath_coreipc::rpc::{WireRequest, WireResponse};
use uipath_coreipc::wire::{Frame, MessageCodec, MessageKind};
use uipath_coreipc::{CancellationToken, RpcError};

type Peer = Framed<DuplexStream, MessageCodec>;

fn request(id: &str, endpoint: &str, method: &str, params: &[&str]) -> WireRequest {
    WireRequest {
        id: id.into(),
        timeout_in_seconds: 40.0,
        endpoint: endpoint.into(),
        method_name: method.into(),
        parameters: params.iter().map(|s| s.to_string()).collect(),
    }
}

async fn next_request(peer: &mut Peer) -> WireRequest {
    let frame = peer.next().await.expect("frame").expect("ok");
    assert_eq!(frame.kind, MessageKind::Request);
    serde_json::from_slice(&frame.data).unwrap()
}

async fn next_frame(peer: &mut Peer) -> Frame {
    peer.next().await.expect("frame").expect("ok")
}

async fn respond(peer: &mut Peer, resp: WireResponse) {
    peer.send(resp.to_frame().unwrap()).await.unwrap();
}

#[tokio::test]
async fn next_request_id_is_monotonic_from_zero() {
    let (c, _s) = duplex(1024);
    let ch = RpcChannel::start(c, Arc::new(NoDispatcher));
    assert_eq!(ch.next_request_id(), "0");
    assert_eq!(ch.next_request_id(), "1");
    assert_eq!(ch.next_request_id(), "2");
}

#[tokio::test]
async fn single_call_writes_request_and_resolves_with_response() {
    let (c, s) = duplex(64 * 1024);
    let ch = RpcChannel::start(c, Arc::new(NoDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    let req = request("0", "IAlgebra", "MultiplySimple", &["2", "3"]);
    let call = tokio::spawn({
        let ch = ch.clone();
        async move {
            ch.call_raw(req, Duration::from_secs(5), CancellationToken::new())
                .await
        }
    });

    let got = next_request(&mut peer).await;
    assert_eq!(got.id, "0");
    assert_eq!(got.endpoint, "IAlgebra");
    assert_eq!(got.method_name, "MultiplySimple");
    assert_eq!(got.parameters, vec!["2", "3"]);

    respond(
        &mut peer,
        WireResponse {
            request_id: "0".into(),
            data: Some("6".into()),
            error: None,
        },
    )
    .await;

    let resp = call.await.unwrap().unwrap();
    assert_eq!(resp.data.as_deref(), Some("6"));
}

#[tokio::test]
async fn error_response_maps_to_remote_error() {
    let (c, s) = duplex(64 * 1024);
    let ch = RpcChannel::start(c, Arc::new(NoDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    let req = request("0", "IAlgebra", "Timeout", &[]);
    let call = tokio::spawn({
        let ch = ch.clone();
        async move {
            ch.call_raw(req, Duration::from_secs(5), CancellationToken::new())
                .await
        }
    });

    let _ = next_request(&mut peer).await;
    let err = uipath_coreipc::rpc::IpcError {
        message: "timed out".into(),
        stack_trace: Some("at X".into()),
        type_name: "System.TimeoutException".into(),
        inner_error: None,
    };
    respond(
        &mut peer,
        WireResponse {
            request_id: "0".into(),
            data: None,
            error: Some(err),
        },
    )
    .await;

    match call.await.unwrap() {
        Err(RpcError::Remote(remote)) => assert!(remote.is_type("System.TimeoutException")),
        other => panic!("expected Remote error, got {other:?}"),
    }
}

#[tokio::test]
async fn concurrent_calls_correlate_when_answered_out_of_order() {
    let (c, s) = duplex(64 * 1024);
    let ch = RpcChannel::start(c, Arc::new(NoDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    // Fire three calls; capture their join handles.
    let mut handles = Vec::new();
    for i in 0..3 {
        let ch = ch.clone();
        let req = request(&i.to_string(), "IAlgebra", "Echo", &[&i.to_string()]);
        handles.push(tokio::spawn(async move {
            ch.call_raw(req, Duration::from_secs(5), CancellationToken::new())
                .await
        }));
    }

    // Drain three request frames.
    for _ in 0..3 {
        let _ = next_request(&mut peer).await;
    }

    // Respond out of order: 2, 0, 1 — each carries a distinct Data.
    for id in ["2", "0", "1"] {
        respond(
            &mut peer,
            WireResponse {
                request_id: id.into(),
                data: Some(format!("\"r{id}\"")),
                error: None,
            },
        )
        .await;
    }

    // Each call must receive the response matching its own id.
    for (i, handle) in handles.into_iter().enumerate() {
        let resp = handle.await.unwrap().unwrap();
        assert_eq!(resp.request_id, i.to_string());
        assert_eq!(resp.data.as_deref(), Some(format!("\"r{i}\"").as_str()));
    }
}

#[tokio::test]
async fn timeout_fails_and_emits_cancel_frame() {
    let (c, s) = duplex(64 * 1024);
    let ch = RpcChannel::start(c, Arc::new(NoDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    let req = request("0", "IAlgebra", "Sleep", &["100000"]);
    let call = tokio::spawn({
        let ch = ch.clone();
        async move {
            ch.call_raw(req, Duration::from_millis(50), CancellationToken::new())
                .await
        }
    });

    // Peer reads the request but deliberately never responds.
    let _ = next_request(&mut peer).await;

    match call.await.unwrap() {
        Err(RpcError::Timeout(_)) => {}
        other => panic!("expected Timeout, got {other:?}"),
    }

    // A Cancel frame for the same id should have been sent to the peer.
    let frame = next_frame(&mut peer).await;
    assert_eq!(frame.kind, MessageKind::Cancel);
    let cancel: uipath_coreipc::rpc::WireCancellation =
        serde_json::from_slice(&frame.data).unwrap();
    assert_eq!(cancel.request_id, "0");
}

#[tokio::test]
async fn external_cancellation_fails_and_emits_cancel_frame() {
    let (c, s) = duplex(64 * 1024);
    let ch = RpcChannel::start(c, Arc::new(NoDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    let ct = CancellationToken::new();
    let req = request("0", "IAlgebra", "Sleep", &["100000"]);
    let call = tokio::spawn({
        let ch = ch.clone();
        let ct = ct.clone();
        async move { ch.call_raw(req, Duration::from_secs(60), ct).await }
    });

    let _ = next_request(&mut peer).await;
    ct.cancel();

    match call.await.unwrap() {
        Err(RpcError::Cancelled) => {}
        other => panic!("expected Cancelled, got {other:?}"),
    }
    let frame = next_frame(&mut peer).await;
    assert_eq!(frame.kind, MessageKind::Cancel);
}

#[tokio::test]
async fn disconnect_fails_inflight_and_subsequent_calls() {
    let (c, s) = duplex(64 * 1024);
    let ch = RpcChannel::start(c, Arc::new(NoDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    let req = request("0", "IAlgebra", "Sleep", &["100000"]);
    let call = tokio::spawn({
        let ch = ch.clone();
        async move {
            ch.call_raw(req, Duration::from_secs(60), CancellationToken::new())
                .await
        }
    });

    let _ = next_request(&mut peer).await;
    // Drop the peer => the duplex closes => the channel reader sees EOF.
    drop(peer);

    match call.await.unwrap() {
        Err(RpcError::ConnectionClosed) => {}
        other => panic!("expected ConnectionClosed, got {other:?}"),
    }

    // Subsequent calls fail fast.
    let again = ch
        .call_raw(
            request("1", "IAlgebra", "Echo", &["1"]),
            Duration::from_secs(60),
            CancellationToken::new(),
        )
        .await;
    assert!(matches!(again, Err(RpcError::ConnectionClosed)));
    assert!(ch.is_closed());
}

// ---- Bidirectional: server-initiated requests (callbacks) ----

struct EchoSumDispatcher;

#[async_trait]
impl Dispatcher for EchoSumDispatcher {
    async fn dispatch(
        &self,
        endpoint: &str,
        method: &str,
        params: Vec<String>,
        _ct: CancellationToken,
    ) -> Result<Option<String>, RemoteError> {
        assert_eq!(endpoint, "IArithmetic");
        assert_eq!(method, "Sum");
        // params are double-encoded ints; decode, add, re-encode.
        let x: i64 = serde_json::from_str(&params[0]).unwrap();
        let y: i64 = serde_json::from_str(&params[1]).unwrap();
        Ok(Some((x + y).to_string()))
    }
}

#[tokio::test]
async fn incoming_request_is_dispatched_and_response_sent_back() {
    let (c, s) = duplex(64 * 1024);
    let _ch = RpcChannel::start(c, Arc::new(EchoSumDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    // The "server" peer initiates a request to the client.
    let req = request("callback-1", "IArithmetic", "Sum", &["1", "2"]);
    peer.send(req.to_frame().unwrap()).await.unwrap();

    // The client dispatches and writes back a Response with double-encoded Data.
    let frame = next_frame(&mut peer).await;
    assert_eq!(frame.kind, MessageKind::Response);
    let resp: WireResponse = serde_json::from_slice(&frame.data).unwrap();
    assert_eq!(resp.request_id, "callback-1");
    assert_eq!(resp.data.as_deref(), Some("3"));
    assert!(resp.error.is_none());
}

struct WaitForCancelDispatcher;

#[async_trait]
impl Dispatcher for WaitForCancelDispatcher {
    async fn dispatch(
        &self,
        _endpoint: &str,
        _method: &str,
        _params: Vec<String>,
        ct: CancellationToken,
    ) -> Result<Option<String>, RemoteError> {
        ct.cancelled().await;
        Ok(Some("\"cancelled\"".into()))
    }
}

#[tokio::test]
async fn incoming_cancel_triggers_handler_token() {
    let (c, s) = duplex(64 * 1024);
    let _ch = RpcChannel::start(c, Arc::new(WaitForCancelDispatcher));
    let mut peer = Framed::new(s, MessageCodec::new());

    // Initiate a request the handler will block on until cancelled.
    let req = request("cb", "IArithmetic", "Slow", &[]);
    peer.send(req.to_frame().unwrap()).await.unwrap();

    // Now cancel it.
    let cancel = uipath_coreipc::rpc::WireCancellation {
        request_id: "cb".into(),
    };
    peer.send(cancel.to_frame().unwrap()).await.unwrap();

    // The handler observed the cancellation and produced its response.
    let frame = next_frame(&mut peer).await;
    assert_eq!(frame.kind, MessageKind::Response);
    let resp: WireResponse = serde_json::from_slice(&frame.data).unwrap();
    assert_eq!(resp.request_id, "cb");
    assert_eq!(resp.data.as_deref(), Some("\"cancelled\""));
}
