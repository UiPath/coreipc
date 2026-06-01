//! Phase F — live interop tests against the real `UiPath.CoreIpc.NodeInterop` .NET host.
//!
//! Mirrors the JS client's `end-to-end.test.ts` scenarios so the Rust client is provably
//! wire-compatible with genuine .NET CoreIpc. Each test spawns its own host on a unique pipe.
//!
//! If the host DLL is not built, the tests skip cleanly (so `cargo test` is green without the
//! .NET SDK). Build it first with:
//! `dotnet build ../js/dotnet/UiPath.CoreIpc.NodeInterop/UiPath.CoreIpc.NodeInterop.csproj -c Debug -f net6.0`

#![allow(clippy::bool_assert_comparison)]

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

use async_trait::async_trait;
use serde::{Deserialize, Serialize};
use tokio::sync::Mutex;

use uipath_coreipc::{CancellationToken, Client, IncomingHandler, RemoteError, RpcError};

#[path = "interop/host.rs"]
mod host;
use host::DotNetHost;

/// Spawn a host, connect a client, run `body` under a timeout, then tear the host down.
/// Skips (passes) when the host DLL is unavailable.
async fn run<F, Fut>(body: F)
where
    F: FnOnce(Client) -> Fut,
    Fut: std::future::Future<Output = ()>,
{
    let Some(host) = DotNetHost::try_start().await else {
        eprintln!("interop skipped: NodeInterop host DLL not built");
        return;
    };
    let client = host.connect_client().await;
    let result = tokio::time::timeout(Duration::from_secs(30), body(client)).await;
    let stderr = host.stderr().join("\n");
    host.shutdown().await;
    if result.is_err() {
        panic!("interop test timed out after 30s\nhost stderr:\n{stderr}");
    }
}

fn ct() -> CancellationToken {
    CancellationToken::new()
}

#[tokio::test]
async fn ping_returns_pong() {
    run(|client| async move {
        let pong: String = client
            .call("IAlgebra", "Ping", (), None, ct())
            .await
            .unwrap();
        assert_eq!(pong, "Pong");
    })
    .await;
}

#[tokio::test]
async fn multiply_simple_returns_product() {
    run(|client| async move {
        let result: i32 = client
            .call("IAlgebra", "MultiplySimple", (2, 3), None, ct())
            .await
            .unwrap();
        assert_eq!(result, 6);
    })
    .await;
}

#[tokio::test]
async fn echo_round_trips() {
    run(|client| async move {
        let result: i32 = client
            .call("IAlgebra", "Echo", (7,), None, ct())
            .await
            .unwrap();
        assert_eq!(result, 7);
    })
    .await;
}

#[tokio::test]
async fn second_endpoint_routes() {
    run(|client| async move {
        let pong: String = client
            .call("ICalculus", "Ping", (), None, ct())
            .await
            .unwrap();
        assert_eq!(pong, "Pong");
    })
    .await;
}

#[tokio::test]
async fn concurrent_calls_do_not_block_each_other() {
    run(|client| async move {
        let long_done = Arc::new(AtomicBool::new(false));

        let long_client = client.clone();
        let flag = long_done.clone();
        let long = tokio::spawn(async move {
            let ok: bool = long_client
                .call("IAlgebra", "Sleep", (500,), None, ct())
                .await
                .unwrap();
            assert!(ok);
            flag.store(true, Ordering::SeqCst);
        });

        // A fast call completes while the 500ms one is still in flight.
        let quick: bool = client
            .call("IAlgebra", "Sleep", (1,), None, ct())
            .await
            .unwrap();
        assert!(quick);
        assert_eq!(
            long_done.load(Ordering::SeqCst),
            false,
            "the long call must still be pending"
        );

        long.await.unwrap();
        assert!(long_done.load(Ordering::SeqCst));
    })
    .await;
}

#[tokio::test]
async fn server_thrown_exception_maps_to_remote_error() {
    run(|client| async move {
        let err = client
            .call::<_, bool>("IAlgebra", "Timeout", (), None, ct())
            .await
            .unwrap_err();
        match err {
            RpcError::Remote(remote) => assert!(
                remote.is_type("System.TimeoutException"),
                "unexpected type: {}",
                remote.type_name
            ),
            other => panic!("expected Remote error, got {other:?}"),
        }
    })
    .await;
}

#[tokio::test]
async fn per_call_timeout_fires() {
    run(|client| async move {
        let err = client
            .call::<_, bool>(
                "IAlgebra",
                "Sleep",
                (100_000,),
                Some(Duration::from_millis(250)),
                ct(),
            )
            .await
            .unwrap_err();
        assert!(matches!(err, RpcError::Timeout(_)), "got {err:?}");
    })
    .await;
}

#[tokio::test]
async fn external_cancellation_aborts_call() {
    run(|client| async move {
        let token = ct();
        let call_client = client.clone();
        let call_token = token.clone();
        let handle = tokio::spawn(async move {
            call_client
                .call::<_, bool>("IAlgebra", "Sleep", (100_000,), None, call_token)
                .await
        });
        tokio::time::sleep(Duration::from_millis(250)).await;
        token.cancel();
        match handle.await.unwrap() {
            Err(RpcError::Cancelled) => {}
            other => panic!("expected Cancelled, got {other:?}"),
        }
    })
    .await;
}

// ---- Bidirectional callback (server -> client request) ----

#[derive(Debug, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct IntMessage {
    payload: i32,
}

/// Client-side `IArithmetic` callback contract implementation.
struct Arithmetic {
    received_payload: Arc<Mutex<Option<i32>>>,
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
            "SendMessage" => {
                let msg: IntMessage = serde_json::from_str(&params[0]).map_err(serde_err)?;
                *self.received_payload.lock().await = Some(msg.payload);
                Ok(Some("true".into()))
            }
            "Sum" => {
                let x: i64 = serde_json::from_str(&params[0]).map_err(serde_err)?;
                let y: i64 = serde_json::from_str(&params[1]).map_err(serde_err)?;
                Ok(Some((x + y).to_string()))
            }
            other => Err(RemoteError {
                message: format!("unexpected callback method {other}"),
                stack_trace: None,
                type_name: "System.MissingMethodException".into(),
                inner: None,
            }),
        }
    }
}

fn serde_err(e: serde_json::Error) -> RemoteError {
    RemoteError {
        message: e.to_string(),
        stack_trace: None,
        type_name: "System.Text.Json.JsonException".into(),
        inner: None,
    }
}

#[tokio::test]
async fn server_initiated_callback_round_trips() {
    run(|client| async move {
        let received = Arc::new(Mutex::new(None));
        client.register(
            "IArithmetic",
            Arithmetic {
                received_payload: received.clone(),
            },
        );

        // TestMessage(Message<int>{Payload:7}) makes the server call back into IArithmetic.
        let result: bool = client
            .call(
                "IAlgebra",
                "TestMessage",
                (IntMessage { payload: 7 },),
                None,
                ct(),
            )
            .await
            .unwrap();

        assert!(result);
        assert_eq!(*received.lock().await, Some(7));
    })
    .await;
}

// ---- DTO serialization parity ----

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
struct Dto {
    bool_property: bool,
    int_property: i32,
    string_property: String,
}

#[tokio::test]
async fn dto_round_trips_through_dotnet() {
    run(|client| async move {
        let dto = Dto {
            bool_property: true,
            int_property: 42,
            string_property: "hi".into(),
        };
        let echoed: Dto = client
            .call("IDtoService", "ReturnDto", (dto.clone(),), None, ct())
            .await
            .unwrap();
        assert_eq!(echoed, dto);
    })
    .await;
}

// ---- Environment variable getter (present / absent → null) ----

#[tokio::test]
async fn env_var_present_and_absent() {
    run(|client| async move {
        let present: Option<String> = client
            .call("IEnvironmentVariableGetter", "Get", ("PATH",), None, ct())
            .await
            .unwrap();
        assert!(present.is_some(), "PATH should be set");

        let absent: Option<String> = client
            .call(
                "IEnvironmentVariableGetter",
                "Get",
                ("__definitely_not_a_real_env_var_xyz__",),
                None,
                ct(),
            )
            .await
            .unwrap();
        assert!(absent.is_none());
    })
    .await;
}

// ---- Disconnect handling ----

#[tokio::test]
async fn host_kill_surfaces_as_error_not_hang() {
    run(|client| async move {
        // IBrittleService.Kill() terminates the host process. The call must not hang, and the
        // channel must observe the disconnect and close itself. (Whether the call returns an
        // error or a void Ok before the socket dies is an inherent race; the invariant we
        // assert is "no hang, and the channel closes".)
        let _ = client
            .call::<_, ()>(
                "IBrittleService",
                "Kill",
                (),
                Some(Duration::from_secs(10)),
                ct(),
            )
            .await;
        // The process death closes the pipe; the reader loop should mark the channel closed.
        let mut closed = false;
        for _ in 0..50 {
            if client.is_closed() {
                closed = true;
                break;
            }
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
        assert!(closed, "channel should close after the host is killed");
    })
    .await;
}
