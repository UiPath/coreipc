# Golden fixtures

Exact JSON payloads captured from the genuine `UiPath.CoreIpc.NodeInterop` .NET host (which
serializes with Newtonsoft.Json). They anchor the Rust client's serde behavior to real .NET
output so `golden_tests.rs` can verify parity **offline** (no host required).

| File | What it captures |
|------|------------------|
| `multiply_simple.response.json` | A success `Response` with a double-encoded integer `Data`. |
| `timeout.error.response.json`   | An error `Response` — `IpcError` with `Type=System.TimeoutException`, `StackTrace`, and `InnerError` omitted (null). |
| `return_dto.response.json`      | A `Response` whose `Data` is a double-encoded DTO, locking the PascalCase convention. |

## Provenance

- **Protocol commit:** `8e0b232ec7547c5af8d8f92990aa0c0ecf9ba342` (the pinned target).
- **Host:** `src/Clients/js/dotnet/UiPath.CoreIpc.NodeInterop`, built `-c Debug -f net6.0`.

## Regeneration (deliberate, reviewed)

```sh
dotnet build ../../../js/dotnet/UiPath.CoreIpc.NodeInterop/UiPath.CoreIpc.NodeInterop.csproj -c Debug -f net6.0
cargo test --test golden_capture -- --ignored --nocapture
```

Regenerating overwrites these files; review the diff before committing. They are compiled into
the test binary via `include_str!`, so they cannot silently go missing.
