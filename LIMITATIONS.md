# UiPath.Ipc — Limitations & cross-client interop

Contracts are a **shared agreement** between both peers, and every client *trusts* the contract rather than policing it — this is IPC, not a versioned schema layer like gRPC/protobuf. Below are deliberate boundaries and cross-client behaviour differences worth knowing when mixing the .NET, [Python](src/Clients/python/uipath-ipc), and [TypeScript](src/Clients/js) clients.

**Contract mismatch is undefined behaviour.** A field the peer can't decode, a value-typed return answered with empty `Data`, a wrong-typed result, or an arg-count mismatch may surface as a raw value, `null`/`None`/`undefined`, or an opaque error — no per-case diagnostics. Keep both sides' contracts in sync.

## Wire shape

- **Per-argument JSON envelope.** `Request.Parameters` is a `string[]` — each argument is *independently* JSON-encoded — not a single JSON array (e.g. `["1.5", "\"hi\""]`, not `[1.5, "hi"]`). Any new client must match this.
- **Frame header**: `[uint8 MessageType][int32 LE length]` then UTF-8 JSON.

## Value-type fidelity — precision can be lost across clients

> **TypeScript / JavaScript has no `decimal` type and no 64-bit integer type** — every JSON number becomes an IEEE-754 `double`. So a high-precision `decimal`, *or* an integer past 2⁵³, simply cannot be represented in the TS client and is silently rounded on parse. **Python** has both (`Decimal` + arbitrary-precision `int`) but still loses `decimal` precision *inbound* (see below). **.NET** is exact for both.

| Type | .NET | Python | TypeScript |
| --- | --- | --- | --- |
| `decimal` | number on the wire, parsed **exactly** | sends as **string** (exact out); **inbound a JSON _number_ → `float` first → lossy** past ~15–17 sig digits | **no `decimal` type**; sends/parses as a JS number → **inbound lossy** (double) |
| `Int64`/`long` | exact | exact (arbitrary-precision `int`) | **no 64-bit int**; **lossy past 2⁵³** (JS double, no BigInt path) |
| `double`/`float` | IEEE-754 | IEEE-754 | IEEE-754 |
| `DateTime` | Newtonsoft default (kind/offset as-is, full ticks) | UTC→`Z`; fraction **truncated to µs** (drops 100 ns ticks) | `Date.toJSON` → always `Z`, **ms only**, **offset lost**; inbound stays a string |
| `Guid`/UUID | string | string | string (caller-supplied) |
| `byte[]`/bytes | base64 | base64 (when typed `bytes`) | **no base64 helper — encode/decode yourself** |
| enum | numeric value | numeric value | caller's raw value |

**Net:** a high-precision `decimal` (or large `Int64`) returned from a .NET server **silently loses precision** at a Python (decimal) or TypeScript (decimal + Int64) client. `Guid`/`byte[]`/dates round-trip but with the format caveats above.

**Enum & non-string dictionary keys don't round-trip across clients.** .NET (Newtonsoft) serializes an enum *dictionary key* as its **name**; Python supports **string keys only** (a non-string key fails fast locally); TypeScript passes keys through `JSON.stringify`. Use **string keys** only.

## Cancellation

- **The cancellation _signal_ is out-of-band, not a parameter value.** Cancellation rides a separate `CancellationRequest` frame keyed by request id (or is local-only) — the token's value never travels. A `CancellationToken` parameter does still occupy its positional **slot** on the wire, but only as an **inert placeholder**: .NET serializes it as an empty string, TypeScript as `{}`, and Python omits the slot entirely. The receiver ignores the slot's content (the server injects the live token by type), so the placeholder has no effect on behaviour.
- **Keep the `CancellationToken` last _only_ when a Python client is in the mix.** .NET and TypeScript accept it at **any** position — it's matched **by type** (the client blanks whatever wire slot holds it; the .NET server injects the live token by type, ignoring the slot value), so e.g. a `Funky(CancellationToken, int, int)` contract round-trips .NET↔.NET. (The would-be validator `Validator.CheckCancellationToken` exists but is **not wired into registration**, so it doesn't enforce a position.) **Python** is the exception: it omits the token from the signature entirely, so a non-last token misaligns the remaining arguments on the wire — so when a Python peer is involved, the token must be the **last** parameter.
- **Propagation to the peer differs:**
  - **.NET** — a fired token sends a `CancellationRequest`.
  - **Python** — sends one **only for methods marked `@ipc_cancellable`** (otherwise the cancel is local-only); a hosted handler honors an inbound cancel only when its contract method is `@ipc_cancellable`.
  - **TypeScript** — as a **caller**, a fired token now sends a `CancellationRequest` to the peer (like .NET), so the remote actually stops; a token already cancelled *before* the call suppresses the request entirely (the remote never runs it), also mirroring .NET. As a **callee** (a hosted callback), it **honors an inbound cancel**: the running handler's per-call token fires, and — when the callback contract declares a trailing `CancellationToken`/`AbortSignal` — the live token (or a bridged `AbortSignal`) is injected so the handler can observe it. A callback registered by bare endpoint name carries no parameter-type metadata, so its arguments are left untouched (the cancel still fires the token, but nothing is injected into the handler).
- A successful response that arrives before the cancel is delivered — the result wins.

## Transports & features

| | .NET | Python | TypeScript |
| --- | --- | --- | --- |
| Named Pipes | ✅ | ✅ | ✅ (Node) |
| TCP/IP | ✅ | ✅ | ❌ |
| WebSocket | ✅ | ❌ (roadmap) | ✅ (Node + Web) |
| Streams (Upload/Download) | ✅ | ❌ (frame fails closed) | ❌ |
| Custom transport | ✅ | ✅ (subclass `ClientTransport`) | — |

**Max message size:** the .NET *server* caps inbound at **2 MB** (configurable) but the .NET *client* inbound is effectively **uncapped** (`int.MaxValue`); Python defaults to a **2 MB** inbound cap — configurable per `IpcClient`/`IpcServer` via `max_message_size` (oversized/negative length rejected before allocation); **TypeScript has no cap** (plus a signed-int32 length-header issue for ≥ 2 GB frames). Treat large inbound payloads as a memory-DoS risk on the .NET/TS clients.

## Arguments & methods

- **Argument count must match.** .NET throws on too many / fills defaults (zero/null) on too few; Python ignores extra trailing args and raises `TypeError` on a missing *required* arg; TypeScript does **no** client-side validation (a mismatch only surfaces at the server).
- **No variadic spreading.** C# `params`, Python `*args`, and JS rest aren't a wire feature — pass explicit, individually-typed parameters (Python `*args` arrive undecoded).
- **Value-type nullability isn't enforced** anywhere — a `null`/omitted value-type argument becomes the default/zero, not an error.
- **Generic methods are unsupported** (.NET throws at resolution).
- Contracts are **interfaces**; methods return `Task`/`Task<T>`. A non-generic `Task` (C#) / `-> None` (Python) is **one-way fire-and-forget** — acked immediately, failures only logged.

## Per-client specifics

- **Python** — [`uipath-ipc` README → Not supported / undefined behaviour](src/Clients/python/uipath-ipc/README.md).
- **TypeScript** — [`src/Clients/js`](src/Clients/js).
