# uipath-coreipc

A Rust client for the [UiPath CoreIpc](https://github.com/UiPath/coreipc) RPC protocol,
sibling to the existing .NET and JavaScript clients. It speaks the CoreIpc wire format over
Windows named pipes and Unix domain sockets (macOS), with `tokio`-based async, bidirectional
RPC (client calls **and** server-initiated callbacks/events).

> Status: in development. See `DESIGN.md` for the authoritative protocol description and the
> implementation plan.

## Layering

| Layer        | Module        | Responsibility |
|--------------|---------------|----------------|
| Wire         | `wire`        | Length-prefixed frame codec `[type:u8][length:i32 LE][data]`. |
| RPC          | `rpc`         | JSON envelope structs, error mapping, the bidirectional channel. |
| Transport    | `transport`   | Named-pipe (Windows) / UDS (Unix) connection. |
| Connect      | `connect`     | Pluggable connect strategy (mirrors `ConnectHelper`). |
| Client       | `client`      | High-level typed `call` / callback registration. |

## Testing

```sh
# Hermetic unit + loopback tests (no .NET required):
cargo test

# Live interop tests against the .NET NodeInterop host (Windows; requires the .NET SDK).
# Build the host first, then run the interop suite:
dotnet build ../js/dotnet/UiPath.CoreIpc.NodeInterop/UiPath.CoreIpc.NodeInterop.csproj -c Debug -f net6.0
cargo test --test interop_tests
```

## License

MIT (see repository root `LICENSE`).
