//! Test harness: spawns the real `UiPath.CoreIpc.NodeInterop` .NET host, waits for its
//! `ReadyToConnect` stdout signal, and guarantees the child is killed when dropped.
//!
//! This is the Rust counterpart of the JS suite's `DotNetProcess.ts` / `CoreIpcServer.ts`.

#![allow(dead_code)]

use std::path::PathBuf;
use std::process::Stdio;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde::Deserialize;
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::process::{Child, Command};
use tokio::sync::oneshot;

use uipath_coreipc::{Client, ClientOptions};

static COUNTER: AtomicU64 = AtomicU64::new(0);

/// One `###`-prefixed control signal emitted on the host's stdout (`Signalling.cs`).
#[derive(Debug, Deserialize)]
struct Signal {
    #[serde(rename = "Kind")]
    kind: String,
    #[serde(rename = "Details", default)]
    details: Option<serde_json::Value>,
}

/// Locate the built NodeInterop DLL, mirroring the JS `Paths.ts` convention.
///
/// Honors `NodeJS_NetCoreAppTargetDir_RelativePath` (the existing CI variable), else falls
/// back to the conventional Debug/net6.0 output under the JS client's `dotnet/` tree.
pub fn dll_path() -> PathBuf {
    let manifest = env!("CARGO_MANIFEST_DIR"); // .../src/Clients/rust
    let js_dir = PathBuf::from(manifest)
        .parent()
        .expect("Clients dir")
        .join("js");
    let rel = std::env::var("NodeJS_NetCoreAppTargetDir_RelativePath")
        .unwrap_or_else(|_| "dotnet/UiPath.CoreIpc.NodeInterop/bin/Debug/net6.0".to_string());
    js_dir.join(rel).join("UiPath.CoreIpc.NodeInterop.dll")
}

/// True when the host DLL has been built and interop tests can run.
pub fn host_available() -> bool {
    dll_path().is_file()
}

/// A guard owning the spawned host process and its pipe name.
pub struct DotNetHost {
    child: Child,
    pub pipe_name: String,
    stderr: Arc<Mutex<Vec<String>>>,
}

impl DotNetHost {
    /// Spawn the host on a unique pipe and await `ReadyToConnect`.
    ///
    /// Panics with the host's stderr on failure. Returns `None` only if the DLL is absent (so
    /// callers can skip cleanly on machines without the built host / .NET SDK).
    pub async fn try_start() -> Option<DotNetHost> {
        if !host_available() {
            return None;
        }
        Some(Self::start().await)
    }

    async fn start() -> DotNetHost {
        let dll = dll_path();
        let pipe_name = unique_pipe_name();

        let mut child = Command::new("dotnet")
            .arg(&dll)
            .arg("--pipe")
            .arg(&pipe_name)
            // No net6.0 runtime may be installed; allow rolling forward to a newer major.
            .env("DOTNET_ROLL_FORWARD", "LatestMajor")
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .stdin(Stdio::null())
            .kill_on_drop(true)
            .spawn()
            .expect("failed to spawn dotnet NodeInterop host");

        let stdout = child.stdout.take().expect("piped stdout");
        let stderr_pipe = child.stderr.take().expect("piped stderr");

        // Collect stderr for diagnostics.
        let stderr = Arc::new(Mutex::new(Vec::<String>::new()));
        {
            let stderr = stderr.clone();
            tokio::spawn(async move {
                let mut lines = BufReader::new(stderr_pipe).lines();
                while let Ok(Some(line)) = lines.next_line().await {
                    stderr.lock().unwrap().push(line);
                }
            });
        }

        // Parse stdout signals; resolve on the first ReadyToConnect (or a Throw).
        let (ready_tx, ready_rx) = oneshot::channel::<Option<serde_json::Value>>();
        tokio::spawn(async move {
            let mut lines = BufReader::new(stdout).lines();
            let mut ready_tx = Some(ready_tx);
            while let Ok(Some(line)) = lines.next_line().await {
                if let Some(rest) = line.strip_prefix("###") {
                    if let Ok(signal) = serde_json::from_str::<Signal>(rest) {
                        match signal.kind.as_str() {
                            "ReadyToConnect" => {
                                if let Some(tx) = ready_tx.take() {
                                    let _ = tx.send(signal.details);
                                }
                            }
                            "Throw" => {
                                if let Some(tx) = ready_tx.take() {
                                    // Surface as a failure: non-null details => panic below.
                                    let _ = tx.send(Some(
                                        signal
                                            .details
                                            .unwrap_or(serde_json::Value::String("Throw".into())),
                                    ));
                                }
                            }
                            _ => {}
                        }
                    }
                }
            }
        });

        let details = match tokio::time::timeout(Duration::from_secs(60), ready_rx).await {
            Ok(Ok(details)) => details,
            Ok(Err(_)) => panic!("host stdout closed before ReadyToConnect"),
            Err(_) => panic!("host did not signal ReadyToConnect within 60s"),
        };
        if let Some(details) = details {
            if !details.is_null() {
                let errs = stderr.lock().unwrap().join("\n");
                panic!("host failed to start: {details}\nstderr:\n{errs}");
            }
        }

        DotNetHost {
            child,
            pipe_name,
            stderr,
        }
    }

    /// Connect a client to this host using the platform default transport.
    pub async fn connect_client(&self) -> Client {
        Client::connect(&self.pipe_name, ClientOptions::default())
            .await
            .expect("client failed to connect to host")
    }

    /// Collected stderr lines (for diagnostics).
    pub fn stderr(&self) -> Vec<String> {
        self.stderr.lock().unwrap().clone()
    }

    /// Kill the host and await its exit (deterministic cleanup on the happy path).
    pub async fn shutdown(mut self) {
        let _ = self.child.start_kill();
        let _ = self.child.wait().await;
    }
}

fn unique_pipe_name() -> String {
    let n = COUNTER.fetch_add(1, Ordering::SeqCst);
    format!("uipath-coreipc-rust-test-{}-{}", std::process::id(), n)
}
