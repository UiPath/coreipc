//! Phase D — high-level `Client` tests over an in-memory loopback.
//!
//! Proves the typed `call` path (tuple args → double-encoded `Parameters`, `Data` → `R`), the
//! void return, the full callback round-trip (server → client request → typed handler →
//! double-encoded response), and graceful shutdown.

use std::sync::Arc;
use std::time::Duration;

use async_trait::async_trait;
use futures_util::{SinkExt, StreamExt};
use tokio::io::{duplex, DuplexStream};
use tokio::sync::Mutex;
use tokio_util::codec::Framed;

use uipath_coreipc::rpc::{WireRequest, WireResponse};
use uipath_coreipc::wire::{MessageCodec, MessageKind};
use uipath_coreipc::{
    CancellationToken, Client, ClientOptions, IncomingHandler, RemoteError, RpcError,
};

fn client(stream: DuplexStream) -> Client {
    Client::from_stream(stream, ClientOptions::default())
}

#[tokio::test]
async fn typed_call_encodes_args_and_decodes_result() {
    let (c, s) = duplex(64 * 1024);
    let client = client(c);
    let mut peer = Framed::new(s, MessageCodec::new());

    let call = tokio::spawn({
        let client = client.clone();
        async move {
            client
                .call::<_, i32>(
                    "IAlgebra",
                    "MultiplySimple",
                    (2, 3),
                    None,
                    CancellationToken::new(),
                )
                .await
        }
    });

    let frame = peer.next().await.unwrap().unwrap();
    assert_eq!(frame.kind, MessageKind::Request);
    let req: WireRequest = serde_json::from_slice(&frame.data).unwrap();
    assert_eq!(req.endpoint, "IAlgebra");
    assert_eq!(req.method_name, "MultiplySimple");
    assert_eq!(req.parameters, vec!["2", "3"]);
    assert_eq!(req.timeout_in_seconds, 40.0);

    peer.send(
        WireResponse {
            request_id: req.id.clone(),
            data: Some("6".into()),
            error: None,
        }
        .to_frame()
        .unwrap(),
    )
    .await
    .unwrap();

    assert_eq!(call.await.unwrap().unwrap(), 6);
}

#[tokio::test]
async fn void_call_decodes_unit_from_null_data() {
    let (c, s) = duplex(64 * 1024);
    let client = client(c);
    let mut peer = Framed::new(s, MessageCodec::new());

    let call = tokio::spawn({
        let client = client.clone();
        async move {
            client
                .call::<_, ()>("IAlgebra", "Sleep", (1,), None, CancellationToken::new())
                .await
        }
    });

    let frame = peer.next().await.unwrap().unwrap();
    let req: WireRequest = serde_json::from_slice(&frame.data).unwrap();
    // Void return: the server omits Data entirely.
    peer.send(
        WireResponse {
            request_id: req.id,
            data: None,
            error: None,
        }
        .to_frame()
        .unwrap(),
    )
    .await
    .unwrap();

    call.await.unwrap().unwrap();
}

#[tokio::test]
async fn remote_error_propagates_to_call() {
    let (c, s) = duplex(64 * 1024);
    let client = client(c);
    let mut peer = Framed::new(s, MessageCodec::new());

    let call = tokio::spawn({
        let client = client.clone();
        async move {
            client
                .call::<_, bool>("IAlgebra", "Timeout", (), None, CancellationToken::new())
                .await
        }
    });

    let frame = peer.next().await.unwrap().unwrap();
    let req: WireRequest = serde_json::from_slice(&frame.data).unwrap();
    peer.send(
        WireResponse {
            request_id: req.id,
            data: None,
            error: Some(uipath_coreipc::rpc::IpcError {
                message: "nope".into(),
                stack_trace: None,
                type_name: "System.TimeoutException".into(),
                inner_error: None,
            }),
        }
        .to_frame()
        .unwrap(),
    )
    .await
    .unwrap();

    match call.await.unwrap() {
        Err(RpcError::Remote(e)) => assert!(e.is_type("System.TimeoutException")),
        other => panic!("expected Remote, got {other:?}"),
    }
}

/// A typed `IArithmetic` callback handler: decodes `(i64, i64)` and returns their sum, and
/// records the last call it received so the test can assert dispatch happened.
struct Arithmetic {
    last_sum_args: Arc<Mutex<Option<(i64, i64)>>>,
}

#[async_trait]
impl IncomingHandler for Arithmetic {
    async fn invoke(
        &self,
        method: &str,
        params: Vec<String>,
        _ct: CancellationToken,
    ) -> Result<Option<String>, RemoteError> {
        match method {
            "Sum" => {
                let x: i64 = serde_json::from_str(&params[0]).unwrap();
                let y: i64 = serde_json::from_str(&params[1]).unwrap();
                *self.last_sum_args.lock().await = Some((x, y));
                Ok(Some((x + y).to_string()))
            }
            other => Err(RemoteError {
                message: format!("no method {other}"),
                stack_trace: None,
                type_name: "System.MissingMethodException".into(),
                inner: None,
            }),
        }
    }
}

#[tokio::test]
async fn registered_callback_handles_server_initiated_request() {
    let (c, s) = duplex(64 * 1024);
    let client = client(c);
    let mut peer = Framed::new(s, MessageCodec::new());

    let recorded = Arc::new(Mutex::new(None));
    client.register(
        "IArithmetic",
        Arithmetic {
            last_sum_args: recorded.clone(),
        },
    );

    // Server initiates IArithmetic.Sum(4, 5).
    peer.send(
        WireRequest {
            id: "cb-1".into(),
            timeout_in_seconds: 40.0,
            endpoint: "IArithmetic".into(),
            method_name: "Sum".into(),
            parameters: vec!["4".into(), "5".into()],
        }
        .to_frame()
        .unwrap(),
    )
    .await
    .unwrap();

    let frame = peer.next().await.unwrap().unwrap();
    assert_eq!(frame.kind, MessageKind::Response);
    let resp: WireResponse = serde_json::from_slice(&frame.data).unwrap();
    assert_eq!(resp.request_id, "cb-1");
    assert_eq!(resp.data.as_deref(), Some("9"));
    assert_eq!(*recorded.lock().await, Some((4, 5)));
}

#[tokio::test]
async fn notify_sends_request_without_awaiting_and_tolerates_orphan_response() {
    let (c, s) = duplex(64 * 1024);
    let client = client(c);
    let mut peer = Framed::new(s, MessageCodec::new());

    // notify returns once the frame is queued — no response is awaited.
    client
        .notify("IUserOperations", "Subscribe", (serde_json::json!({}),))
        .await
        .unwrap();

    // The peer receives the request frame (one param: the empty Message<void> object).
    let frame = peer.next().await.unwrap().unwrap();
    assert_eq!(frame.kind, MessageKind::Request);
    let req: WireRequest = serde_json::from_slice(&frame.data).unwrap();
    assert_eq!(req.endpoint, "IUserOperations");
    assert_eq!(req.method_name, "Subscribe");
    assert_eq!(req.parameters.len(), 1);

    // The server still sends a void Response (our wire mode). It is orphaned (no waiter) and
    // must be silently discarded — the channel stays healthy, proven by a subsequent call.
    peer.send(
        WireResponse { request_id: req.id, data: None, error: None }
            .to_frame()
            .unwrap(),
    )
    .await
    .unwrap();

    let call = tokio::spawn({
        let client = client.clone();
        async move {
            client
                .call::<_, i32>("IAlgebra", "Echo", (7,), None, CancellationToken::new())
                .await
        }
    });
    let frame = peer.next().await.unwrap().unwrap();
    let req: WireRequest = serde_json::from_slice(&frame.data).unwrap();
    assert_eq!(req.method_name, "Echo");
    peer.send(
        WireResponse { request_id: req.id, data: Some("7".into()), error: None }
            .to_frame()
            .unwrap(),
    )
    .await
    .unwrap();
    assert_eq!(call.await.unwrap().unwrap(), 7);
}

#[tokio::test]
async fn shutdown_drains_pending_calls() {
    let (c, s) = duplex(64 * 1024);
    let client = client(c);
    let mut peer = Framed::new(s, MessageCodec::new());

    let call = tokio::spawn({
        let client = client.clone();
        async move {
            client
                .call::<_, i32>(
                    "IAlgebra",
                    "Echo",
                    (1,),
                    Some(Duration::from_secs(60)),
                    CancellationToken::new(),
                )
                .await
        }
    });

    // Let the request reach the peer, then shut the client down.
    let _ = peer.next().await.unwrap().unwrap();
    client.shutdown();

    assert!(matches!(
        call.await.unwrap(),
        Err(RpcError::ConnectionClosed)
    ));
    assert!(client.is_closed());
}
