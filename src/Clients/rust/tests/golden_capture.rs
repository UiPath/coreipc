//! Golden-fixture capture (run explicitly, not in CI).
//!
//! Connects raw (codec over the transport, below the high-level client) to the live .NET host
//! and writes the *exact* JSON payloads it emits into `tests/fixtures/`. These committed
//! artifacts let `golden_tests.rs` verify serde parity offline, anchored to genuine Newtonsoft
//! output. Regeneration is deliberate:
//!
//! ```text
//! dotnet build ../js/dotnet/UiPath.CoreIpc.NodeInterop/UiPath.CoreIpc.NodeInterop.csproj -c Debug -f net6.0
//! cargo test --test golden_capture -- --ignored --nocapture
//! ```

#![allow(dead_code)]

use std::path::PathBuf;
use std::time::Duration;

use futures_util::{SinkExt, StreamExt};
use tokio_util::codec::Framed;
use uipath_coreipc::rpc::WireRequest;
use uipath_coreipc::transport::default_transport;
use uipath_coreipc::wire::MessageCodec;
use uipath_coreipc::CancellationToken;

#[path = "interop/host.rs"]
mod host;
use host::DotNetHost;

fn fixtures_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("fixtures")
}

#[tokio::test]
#[ignore = "regenerates golden fixtures from the live .NET host; run explicitly"]
async fn capture_golden_fixtures() {
    let host = DotNetHost::try_start()
        .await
        .expect("host DLL must be built to capture fixtures");

    let transport = default_transport(&host.pipe_name);
    let stream = transport
        .connect(Duration::from_secs(10), CancellationToken::new())
        .await
        .expect("raw connect");
    let mut framed = Framed::new(stream, MessageCodec::new());

    let dir = fixtures_dir();
    std::fs::create_dir_all(&dir).unwrap();

    async fn capture(
        framed: &mut Framed<uipath_coreipc::transport::BoxStream, MessageCodec>,
        dir: &std::path::Path,
        file: &str,
        req: WireRequest,
    ) {
        framed.send(req.to_frame().unwrap()).await.unwrap();
        let frame = framed.next().await.expect("frame").expect("ok");
        let json = String::from_utf8(frame.data.to_vec()).unwrap();
        eprintln!("{file}: {json}");
        std::fs::write(dir.join(file), json).unwrap();
    }

    capture(
        &mut framed,
        &dir,
        "multiply_simple.response.json",
        WireRequest {
            id: "0".into(),
            timeout_in_seconds: 40.0,
            endpoint: "IAlgebra".into(),
            method_name: "MultiplySimple".into(),
            parameters: vec!["2".into(), "3".into()],
        },
    )
    .await;

    capture(
        &mut framed,
        &dir,
        "timeout.error.response.json",
        WireRequest {
            id: "1".into(),
            timeout_in_seconds: 40.0,
            endpoint: "IAlgebra".into(),
            method_name: "Timeout".into(),
            parameters: vec![],
        },
    )
    .await;

    capture(
        &mut framed,
        &dir,
        "return_dto.response.json",
        WireRequest {
            id: "2".into(),
            timeout_in_seconds: 40.0,
            endpoint: "IDtoService".into(),
            method_name: "ReturnDto".into(),
            parameters: vec![
                r#"{"BoolProperty":true,"IntProperty":42,"StringProperty":"hi"}"#.into(),
            ],
        },
    )
    .await;

    host.shutdown().await;
}
