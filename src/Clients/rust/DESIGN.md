# Design: Upstream Rust CoreIpc Client

- **Date:** 2026-06-01
- **Status:** Draft for review (handoff to a dedicated implementation session)
- **Repo:** `github.com/UiPath/coreipc` — new client at `src/Clients/rust/` (sibling to `src/Clients/js` and the .NET client)
- **Protocol pinned at:** `UiPath/coreipc@8e0b232ec7547c5af8d8f92990aa0c0ecf9ba342` (the commit that produced `@uipath/coreipc@2.5.1-20241212-01`, the version the Assistant consumes)

## 1. Why this exists

The UiPath Assistant is being migrated off Electron to Tauri (Approach B: full Rust host). The Assistant's single hardest dependency is `@uipath/coreipc` — the named-pipe RPC client it uses to talk to the local .NET Robot service (sign-in, job start/stop/pause, process lists, telemetry, update service, plus pushed event streams). A Tauri/Rust host needs a Rust CoreIpc client.

This client is built **upstream in `UiPath/coreipc`** (not as a private fork inside Assistant) so it is a first-class, maintained client alongside the existing .NET and JS clients, and protocol drift is owned by the CoreIpc team. It must be **complete and fully tested before any Assistant migration work begins** — it is the feasibility gate for the whole Approach B effort.

This spec scopes **only the CoreIpc Rust client**. The Assistant migration and the robot-client contract surface are separate, later sub-projects.

## 2. Goals / non-goals

**Goals**
- A `coreipc` Rust crate implementing the CoreIpc wire protocol as it exists at the pinned commit.
- Async, `tokio`-based. Bidirectional RPC (client-initiated calls **and** server-initiated callbacks/events).
- Transports: Windows named pipes and macOS (Unix domain socket) — matching what the .NET Robot listens on.
- Wire-compatible with genuine .NET CoreIpc, proven by an integration test suite that round-trips against the existing **.NET interop test host**.
- Faithful reproduction of .NET JSON serialization conventions (PascalCase fields, parameter/return double-encoding, error shape).

**Non-goals**
- The robot-client contract surface (specific `Endpoint`/`MethodName`/DTOs the Assistant calls). The crate ships only a **thin contract slice** needed to exercise/test the protocol; the full surface is grown demand-driven during the Assistant migration.
- The WebSockets transport and the browser/`web` target (the Assistant↔Robot link is named-pipe/UDS only). Leave the transport layer abstracted so WS can be added later, but do not implement it now.
- Any Tauri or Assistant code.

## 3. The wire protocol (authoritative, from the pinned source)

### 3.1 Framing — `std/core/Protocol/Network/MessageStream.ts`
Length-prefixed frames over the raw stream:

```
┌────────────┬─────────────────────┬──────────────────────────┐
│ type: u8   │ length: i32 (LE)    │ data: length bytes (UTF-8 JSON) │
└────────────┴─────────────────────┴──────────────────────────┘
```
- `type`: `0 = Request`, `1 = Response`, `2 = Cancel` (`Network.Message.Type`).
- `length`: little-endian `int32`, byte length of `data`.
- `data`: UTF-8 JSON of the RPC message.
- Reads are "read fully" loops (read exactly 1, then 4, then `length` bytes).

### 3.2 Messages — `std/core/Protocol/Rpc/RpcMessage.ts` (JSON, PascalCase)
- **Request** → frame type 0:
  ```json
  { "Id": "0", "TimeoutInSeconds": 40, "Endpoint": "<contract>", "MethodName": "<method>", "Parameters": ["<json-arg-0>", "<json-arg-1>"] }
  ```
- **Response** → frame type 1:
  ```json
  { "RequestId": "0", "Data": "<json-return>|null", "Error": <IpcError>|null }
  ```
- **CancellationRequest** → frame type 2:
  ```json
  { "RequestId": "0" }
  ```
- **IpcError** — `std/core/Protocol/Errors/IpcError.ts`:
  ```json
  { "Message": "...", "StackTrace": "...", "Type": "<.NET exception type>", "InnerError": <IpcError>|null }
  ```

### 3.3 Serialization conventions (the parity-sensitive part)
- **Field names are PascalCase exactly** as above (`Id`, `TimeoutInSeconds`, `Endpoint`, `MethodName`, `Parameters`, `RequestId`, `Data`, `Error`, and `Message`/`StackTrace`/`Type`/`InnerError`).
- **Double-encoding**: each element of `Parameters` is itself the JSON-serialized form of one argument (a `string`), and `Response.Data` is the JSON-serialized form of the return value (a `string`, or `null` for void/null). The outer envelope is JSON; the args/return are JSON-within-JSON.
- The deeper DTO payloads (inside the double-encoded strings) must match **.NET's** serializer conventions used by the Robot. Establish the exact convention (System.Text.Json vs Newtonsoft, casing, enum-as-string vs int, `DateTime`/`DateTimeOffset` format, null handling) empirically in Phase 0 against the .NET host, and encode it as a single shared serde configuration.

### 3.4 RPC semantics — `RpcChannel.ts`, `RpcCallContext.ts`
- **Outgoing call correlation**: monotonic integer `Id` rendered as a string (`"0"`, `"1"`, …) per channel; an `Id → pending` table. `call()` registers the pending entry, writes the Request, awaits the matching Response by `RequestId`, then removes the entry.
- **Bidirectional / callbacks**: the channel is symmetric — the **server also sends Request frames to the client** to deliver event streams / callbacks (`IncommingInitiatingRpcMessage = Request | CancellationRequest`). Incoming Requests are surfaced as `RpcCallContext.Incomming { request, respond(response) }`; the consumer dispatches by `Endpoint`/`MethodName` and sends a Response back. This is how `JobStatusChanged`, `ProcessListUpdated`, etc. arrive.
- **Cancellation**: a `Cancel` frame carries a `RequestId`; on the receiving side it cancels the corresponding in-flight handler.
- **Timeouts**: per-call `TimeoutInSeconds`; the outgoing call fails if no Response arrives in time.

### 3.5 Connection model — `ConnectHelper.ts`, `ConnectContext.ts`
- **No application-level handshake.** `defaultConnectHelper` simply calls `tryConnectAsync()` (connect the socket). The first frame sent is a normal Request. `ConnectHelper` is an extensibility hook (custom retry / launch-the-server strategy) — model it as an optional injected async connect strategy, default = "just connect."

### 3.6 Transport
- **Address** is opaque to the protocol; `NamedPipeAddress.key = "namedpipe:<name>"`. The protocol layer takes a connected stream; transport resolves a name to a stream.
- **Windows:** named pipe at `\\.\pipe\<name>` (`tokio::net::windows::named_pipe::ClientOptions`).
- **macOS (TO CONFIRM in Phase 0):** .NET implements named pipes on Unix over a Unix domain socket at `/tmp/CoreFxPipe_<name>`. Expectation is the Rust client connects to that UDS path. **Must be verified against the live mac Robot** before relying on it.

## 4. Proposed Rust crate

### 4.1 Layout (`src/Clients/rust/`)
```
src/Clients/rust/
├── Cargo.toml                 # crate: uipath-coreipc (or per upstream naming)
├── README.md
├── src/
│   ├── lib.rs
│   ├── wire/
│   │   ├── codec.rs           # MessageCodec: tokio_util Encoder/Decoder (u8 + i32le + bytes)
│   │   └── message.rs         # Message { kind: MessageKind, data: Bytes }, MessageKind enum
│   ├── rpc/
│   │   ├── messages.rs        # Request / Response / CancellationRequest serde structs, IpcError
│   │   ├── channel.rs         # RpcChannel: read loop, outgoing correlation table, incoming dispatch
│   │   └── error.rs           # RemoteError, IpcError → Rust error mapping
│   ├── serde_conv.rs          # shared serde config + double-encode/decode helpers
│   ├── transport/
│   │   ├── mod.rs             # Transport trait (connect(name) -> AsyncRead+AsyncWrite)
│   │   ├── named_pipe.rs      # #[cfg(windows)]
│   │   └── uds.rs             # #[cfg(unix)] /tmp/CoreFxPipe_<name>
│   ├── connect.rs             # ConnectHelper hook, default connect strategy, timeouts
│   └── client.rs              # high-level client: call<TArgs, TRet>(endpoint, method, args), callback registry
└── tests/
    ├── codec_tests.rs
    ├── serde_tests.rs
    └── interop/               # integration tests vs .NET NodeInterop host
```

### 4.2 Core types / API sketch (illustrative, not final)
```rust
pub enum MessageKind { Request = 0, Response = 1, Cancel = 2 }
pub struct Message { pub kind: MessageKind, pub data: Bytes }

// rpc/messages.rs — exact wire field names
#[derive(Serialize, Deserialize)]
pub struct Request { pub Id: String, pub TimeoutInSeconds: f64, pub Endpoint: String,
                     pub MethodName: String, pub Parameters: Vec<String> }
#[derive(Serialize, Deserialize)]
pub struct Response { pub RequestId: String, pub Data: Option<String>, pub Error: Option<IpcError> }
#[derive(Serialize, Deserialize)]
pub struct CancellationRequest { pub RequestId: String }
#[derive(Serialize, Deserialize)]
pub struct IpcError { pub Message: String, pub StackTrace: String, pub Type: String,
                      pub InnerError: Option<Box<IpcError>> }

// client.rs
impl Client {
    async fn call<A: Serialize, R: DeserializeOwned>(
        &self, endpoint: &str, method: &str, args: &[A], timeout: Duration,
    ) -> Result<R, RemoteError>;

    // register a handler for server-initiated callbacks on an endpoint
    fn on_callback(&self, endpoint: &str, handler: impl IncomingHandler);
}
```
- `call` double-encodes each arg (`serde_json::to_string`) into `Parameters`, awaits the `Response`, maps `Error` → `RemoteError`, and `serde_json::from_str`-decodes `Data` into `R`.
- Read loop demuxes frames: Response → complete the pending oneshot by `RequestId`; Request → dispatch to the callback registry, run handler, send a Response; Cancel → cancel the matching handler.

### 4.3 Dependencies (proposed)
`tokio` (rt, net, io, sync, time), `tokio-util` (codec), `bytes`, `serde`, `serde_json`, `thiserror`, `tracing`. Windows named pipes via tokio's `windows::named_pipe`. Keep the dependency set minimal and justify each in review.

## 5. Contract-surface strategy (thin slice only)
Implement just enough contract to exercise and test the protocol:
- Whatever endpoints the **NodeInterop .NET host** exposes (its `Contracts.cs` / `ServiceImpls.cs` / `Signalling.cs` define the test contracts the JS suite uses) — mirror those.
- Do **not** port robot-client's contracts here. Those are added later, demand-driven, during the Assistant migration (and likely in the Assistant repo or a separate contracts crate, TBD then).

## 6. Test strategy (primary success criterion)
The user requirement is "full tests to confirm functionality." Layers:

1. **Unit — wire codec** (`codec_tests.rs`): encode/decode round-trips; exact byte layout (`[type][i32le len][data]`); partial/chunked reads reassemble; oversized/zero-length; type enum mapping. Include **golden-byte** fixtures captured from the JS/.NET implementation.
2. **Unit — serde** (`serde_tests.rs`): Request/Response/Cancel/IpcError JSON matches byte-for-byte (modulo property order) against fixtures from the JS client; double-encode/decode of params and `Data`; `Error` mapping to `RemoteError`; `InnerError` nesting.
3. **Integration — against the real .NET host** (`tests/interop/`): launch the existing **`UiPath.CoreIpc.NodeInterop`** .NET host (the same one the JS client tests round-trip against) and exercise:
   - client→server calls (sync return, void, error-throwing method → `RemoteError`, cancellation, timeout),
   - server→client callbacks / streamed signals (`Signalling.cs`),
   - concurrent in-flight calls (correlation correctness),
   - connect via `ConnectHelper` default and a custom strategy.
   **Mirror the JS client's existing test scenarios** so behavior is provably equivalent to the shipping client.
4. **Cross-platform CI**: run unit + integration on **Windows and macOS** runners (the two target Assistant platforms). The mac run is what validates the UDS path assumption (§3.6).
5. **Parity harness (stretch)**: a small matrix that runs the same scenario through the JS client and the Rust client against the same host and diffs observable behavior.

Coverage target and exact CI wiring to be set in the implementation plan.

## 7. Open questions → resolve in Phase 0 spike (first, before full build-out)
A few days of spike work, before committing to the full crate + test suite:
1. **macOS transport path** — confirm the Robot's pipe is reachable at `/tmp/CoreFxPipe_<name>` (or discover the actual scheme) by connecting to a live mac Robot and/or the .NET host on mac.
2. **DTO serialization convention** — determine empirically (System.Text.Json vs Newtonsoft, casing, enums, `DateTime` format) by round-tripping non-trivial DTOs against the .NET host; lock it into `serde_conv.rs`.
3. **Pipe name source** — confirm how the Robot pipe name is derived (the name itself comes from robot-client config, not coreipc; needed only so the spike can connect to a real Robot — not part of the crate's contract surface).
4. **Upstream conventions** — crate naming, license headers, CI integration, and how `src/Clients/rust` plugs into the repo's existing build/release (the JS/.NET clients are built via the ADO `CoreIpc` pipeline).

The spike's exit criterion = the "Hello Robot" milestone below.

## 8. Milestones
- **M0 — Hello Robot spike:** standalone Rust binary connects to a running Robot (or the .NET host) over CoreIpc, makes one real call and receives one pushed callback, on **both** Windows and macOS. Resolves §7.1–§7.2. *This is the Approach-B feasibility gate.*
- **M1 — Protocol crate:** codec + rpc channel + transports + serde conventions + connect hook, with unit suites (§6.1–§6.2) green.
- **M2 — Interop suite:** integration tests against the NodeInterop .NET host (§6.3) green on Windows + macOS CI (§6.4), mirroring the JS scenarios.
- **M3 — Upstream PR:** `src/Clients/rust` merged into `UiPath/coreipc` with CI, README, and (if applicable) crate publish wiring.

Only after M3 does Assistant migration work begin.

## 9. Risks
- **Hidden serialization quirks** in the DTO payloads (.NET polymorphism, `$type` discriminators, custom converters in robot-client's contracts) — mitigated by testing against the real .NET host early and by deferring the heavy contract surface to the Assistant phase.
- **macOS transport assumption wrong** — mitigated by making it M0 exit criteria, not a late discovery.
- **Protocol drift** — the pinned commit `8e0b232` defines the target; if upstream `master` has since changed the wire format, reconcile during the upstream PR (coordinate with the CoreIpc team on which protocol revision to target).
- **Upstream process latency** — landing code in `UiPath/coreipc` involves their reviewers/CI; budget for it since Assistant work is gated on it.

## 10. Handoff
This spec is written from the Assistant repo for context, but the work happens in a **separate Claude Code session rooted in a fresh `UiPath/coreipc` clone**. That session: reviews/refines this spec → `writing-plans` → TDD implementation → upstream PR. Carry this file over (or paste it) as the starting design.
