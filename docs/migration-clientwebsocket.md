# Migrating `Reown.Core.Network.WebSocket` from `Websocket.Client` to `System.Net.WebSockets.ClientWebSocket`

**Status:** Proposal
**Owner:** Core / Networking
**Target package:** `Reown.Core.Network.WebSocket` (NuGet-only, no UPM distribution)
**Affected versions:** next minor release of `Reown.Core.*`, `Reown.Sign`, `Reown.WalletKit`

---

## 1. Background

`Reown.Core.Network.WebSocket` exposes a single class, `WebsocketConnection`, that adapts a WebSocket transport to the `IJsonRpcConnection` contract. The current implementation wraps `Websocket.Client` (Marfusios) 5.1.2.

The wrapper uses only a small subset of `Websocket.Client`:

| API used | Where |
|---|---|
| `new WebsocketClient(Uri)` | `WebsocketConnection.cs:227` |
| `ReconnectTimeout = null` (disables built-in reconnect) | `WebsocketConnection.cs:228` |
| `Start()` / `Stop(...)` | `WebsocketConnection.cs:230`, `:110` |
| `Send(string)` | `WebsocketConnection.cs:129`, `:150`, `:171` |
| `MessageReceived` / `DisconnectionHappened` (Rx) | `WebsocketConnection.cs:248-249` |
| `NativeClient.State` | `WebsocketConnection.cs:70` |

`Websocket.Client`'s headline features — `IObservable` event surface, auto-reconnect, recyclable receive buffers — are mostly unused. The Reown protocol layer manages reconnects itself, and the only Rx consumer is two `.Subscribe(...)` calls.

### Drivers for migration

1. **Maintenance risk.** Marfusios/websocket-client has had a single non-trivial commit in the last ~24 months (v5.3.0, a 1-line `ConnectTimeout` default change, 2025-09-26). 57 open issues, including a Stop() deadlock (#139), reconnect `ObjectDisposedException` (#154), lost-message-after-reconnect (#148).
2. **Target framework gap.** The package csproj lists `netstandard2.1;net6;net7;net8`. It runs on `net9`/`net10` via compatibility folding only — not tested by upstream. Reown now targets `net9` and `net10`.
3. **Transitive footprint.** Pulls in `System.Reactive` 6.0 (a multi-megabyte assembly per NuGet — exact size order-of-megabytes, not measured by us), `Microsoft.IO.RecyclableMemoryStream` 3.0, and `Microsoft.Extensions.Logging.Abstractions` 8.0 to provide ~50 lines of value over `ClientWebSocket`. This footprint lands in every NuGet consumer of `Reown.Sign` / `Reown.WalletKit`.

### Scope clarification (Unity is not affected)

`Reown.Core.Network.WebSocket` is **only used by NuGet consumers**. Unity builds never load it: `Reown.Sign.Unity` ships its own `IConnectionBuilder` (`ConnectionBuilderUnity.cs:11`) that constructs `WebSocketConnectionUnity`, which uses a vendored fork of `NativeWebSocket` (`src/Reown.Sign.Unity/Runtime/WebSocket.cs`) — `ClientWebSocket` on native platforms, the `ReownWebSocket.jslib` shim on WebGL. `Websocket.Client` and `System.Reactive` never reach IL2CPP. The Unity WebSocket implementation is out of scope for this migration.

### Non-goals

- No change to the public surface of `WebsocketConnection` or `IJsonRpcConnection`.
- No change to the WebGL path (it goes through `ReownWebSocket.jslib` in `Reown.Sign.Unity` and never touches this package).
- No change to reconnect semantics — reconnects continue to be driven by the Sign-layer state machine.

---

## 2. Target design

Replace the internal `WebsocketClient _socket` field with a hand-rolled wrapper around `System.Net.WebSockets.ClientWebSocket`. Keep the `WebsocketConnection` class, its constructor, its events, and all `IJsonRpcConnection` members unchanged.

### 2.1 Components

```
WebsocketConnection (public, unchanged API)
└── internal ClientWebSocketTransport
    ├── ClientWebSocket _client
    ├── Channel<string> _outbox         // single-reader, multi-writer
    ├── Task _receiveLoop
    ├── Task _sendLoop
    ├── CancellationTokenSource _cts    // tied to connection lifetime
    └── events: Opened, PayloadReceived, Closed, ErrorReceived  // matches IJsonRpcConnection
```

Each new transport instance corresponds to one open WebSocket. Reconnection means disposing the transport and constructing a new one — this matches `Websocket.Client`'s behaviour with `ReconnectTimeout = null` and the way Reown's protocol layer already drives reconnects.

### 2.2 Receive loop

Pattern adapted from SignalR's `WebSocketsTransport.cs` (canonical .NET reference):

1. **One rented receive buffer per transport.** `_recvBuffer = ArrayPool<byte>.Shared.Rent(4096)` once at construction; returned in `Dispose`. Re-used across every `ReceiveAsync` call. Use the `Memory<byte>` overload so `ReceiveAsync` returns the struct `ValueWebSocketReceiveResult` (no allocation).
2. Loop:
   - `await _client.ReceiveAsync(_recvBuffer.AsMemory(), _cts.Token)` (or the `ArraySegment` overload on netstandard2.1 — see §2.4).
   - If `MessageType == Close` → trigger graceful close path, exit loop.
   - If `MessageType == Binary` → discard (Reown only sends/receives text JSON, matches current behaviour at `WebsocketConnection.cs:281`).
   - If `EndOfMessage && _reassembly is null` → fast path: decode directly via `Encoding.UTF8.GetString(_recvBuffer.AsSpan(0, result.Count))`. **Skip the event if the decoded string is null/empty/whitespace** (matches `WebsocketConnection.cs:290-291`), otherwise raise `PayloadReceived`. **No intermediate buffer** in the non-empty case.
   - Otherwise → fragmented: append the span to `_reassembly` (see below). When `EndOfMessage`, decode the accumulated bytes once, return reassembly storage to the pool, and apply the same `string.IsNullOrWhiteSpace` filter before raising `PayloadReceived`.
3. On exit:
   - `OperationCanceledException` *and* `_shutdownRequested == true` → normal client-initiated shutdown: raise `Closed` only, do **not** raise `ErrorReceived`.
   - Any other `WebSocketException`, unexpected `OperationCanceledException`, `IOException`, or socket abort → raise `ErrorReceived` followed by `Closed` and exit.

**Whitespace filter, both paths.** The current `Websocket.Client`-based code drops empty/whitespace text frames before they reach the JSON-RPC layer (`WebsocketConnection.cs:290-291`); preserving that is part of the migration's "no observable change" contract. Some relays send keep-alive whitespace frames that would otherwise propagate to `JsonRpcProvider` and fail JSON parsing. Centralise this in a single `RaiseIfNotEmpty(string payload)` helper used by both fast and reassembled paths so the rule is impossible to forget when the loop is touched later.

**Distinguishing shutdown from error.** The transport keeps a private `volatile bool _shutdownRequested` flag (or, equivalently, checks `_cts.IsCancellationRequested` against a snapshot taken before the `await`). `Close()` and `Dispose()` set the flag *before* cancelling `_cts`, so the receive/send loops can correlate an incoming `OperationCanceledException` to an intentional shutdown and skip the `ErrorReceived` notification. The same gate applies to the send loop's catch arm. This avoids the false-positive `ErrorReceived → Closed` sequence that consumers would otherwise see on every graceful `Close()`. The matching test is case 18 (graceful close should emit `Closed` only, never `ErrorReceived`) and case 19 (abrupt TCP RST must still raise `ErrorReceived` before `Closed`).

**Reassembly storage.** Use a small internal `PooledByteBufferWriter` (~50 LOC) that implements `IBufferWriter<byte>` and rents its backing array from `ArrayPool<byte>.Shared`. On `Grow`, it rents a larger array, copies, and returns the old one. This replaces `ArrayBufferWriter<byte>` (which allocates from the GC heap on every grow) and means a long-lived connection with occasional fragmented frames stays at zero steady-state allocations on the receive path.

**0-byte idle probe (optional, recommended):** Per SignalR's pattern, an idle connection can do a zero-length `ReceiveAsync` to detect server-close without keeping a buffer span pinned across the await. Relay connections are long-lived and mostly idle; this lets the rented buffer be returned to the pool during idle periods if memory pressure matters more than per-message latency. Default off; flag in a constant.

### 2.3 Send loop

**Encode at the producer**, transport pooled bytes through the queue:

1. `SendRequest<T>(payload)` serialises to JSON (existing `JsonConvert.SerializeObject`) → rents a `byte[]` from `ArrayPool` sized to `Encoding.UTF8.GetMaxByteCount(json.Length)` → calls `Encoding.UTF8.GetBytes(json, buffer)` → enqueues a `struct PooledSendBuffer { byte[] Array; int Length; }` onto a bounded `Channel<PooledSendBuffer>`.
2. The single send-loop consumer pulls the buffer, calls `_client.SendAsync(buffer.Array.AsMemory(0, buffer.Length), WebSocketMessageType.Text, endOfMessage: true, _cts.Token)`, and returns the array to the pool — even on exception (use `try/finally`).
3. Producers never block waiting for `SendAsync`; they may block waiting for queue capacity, which is the desired back-pressure behaviour.

Use a bounded channel with `FullMode = Wait` and capacity 256. Relay traffic is low-rate JSON-RPC; bounding the queue prevents unbounded memory growth if the network stalls.

**Why encode at the producer (not in the send loop):** producers already hold the string; converting once at submit time means the send loop touches only `ReadOnlyMemory<byte>` and avoids both a heap-allocated `byte[]` (from a naive `Encoding.UTF8.GetBytes(string)` overload) and a string→byte traversal inside the IO-hot critical section. The single existing call to `Websocket.Client`'s `Send(string)` does the encoding inside its own loop and allocates a fresh `byte[]` per send.

### 2.4 Target framework & AOT/trim compatibility

The package's effective TFM set (from `src/Directory.Build.props` → `$(DefaultTargetFrameworks)`) is **`net7.0;net8.0;net9.0;net10.0;netstandard2.1`**. Every primitive in the design above is available on all five:

| Primitive | net7 | net8 | net9 | net10 | netstandard2.1 |
|---|---|---|---|---|---|
| `ClientWebSocket.{Connect,Send,Receive}Async(ArraySegment<byte>, …)` → `Task<WebSocketReceiveResult>` | inbox | inbox | inbox | inbox | inbox |
| `ClientWebSocket.{Send,Receive}Async(Memory<byte>, …)` → `ValueTask<ValueWebSocketReceiveResult>` | inbox | inbox | inbox | inbox | **not in ref assembly** |
| `ValueWebSocketReceiveResult` (struct, no alloc) | inbox | inbox | inbox | inbox | **not in ref assembly** |
| `ClientWebSocketOptions.HttpVersion` / `HttpVersionPolicy` | inbox | inbox | inbox | inbox | **not in ref assembly** |
| `ArrayPool<byte>.Shared`, `IBufferWriter<byte>` | inbox | inbox | inbox | inbox | inbox |
| `Encoding.UTF8.GetString(ROSpan<byte>)` / `GetBytes(string, Span<byte>)` | inbox | inbox | inbox | inbox | inbox |
| `Channel<T>` | inbox | inbox | inbox | inbox | **PackageReference required** |

**Action for netstandard2.1:** add a conditional package reference to the csproj:

```xml
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.1'">
    <PackageReference Include="System.Threading.Channels"/>
</ItemGroup>
```

Add `System.Threading.Channels` (latest 8.0.0+) to `Directory.Packages.props`. On net7+ it's runtime-inbox, so no extra dependency lands on the modern code paths.

**`Memory<byte>` / `ValueWebSocketReceiveResult` are net7+ only.** The netstandard2.1 reference assembly exposes only the `ArraySegment<byte>` + `Task<WebSocketReceiveResult>` overloads on `ClientWebSocket`. The receive and send loops therefore branch on TFM:

```csharp
private static readonly ArraySegment<byte> AsSegment(byte[] buffer, int offset, int count)
    => new ArraySegment<byte>(buffer, offset, count);

// Receive
#if NETSTANDARD2_1
WebSocketReceiveResult result =
    await _client.ReceiveAsync(AsSegment(_recvBuffer, 0, _recvBuffer.Length), _cts.Token)
                  .ConfigureAwait(false);
int count = result.Count;
bool endOfMessage = result.EndOfMessage;
WebSocketMessageType messageType = result.MessageType;
#else
ValueWebSocketReceiveResult result =
    await _client.ReceiveAsync(_recvBuffer.AsMemory(), _cts.Token)
                  .ConfigureAwait(false);
int count = result.Count;
bool endOfMessage = result.EndOfMessage;
WebSocketMessageType messageType = result.MessageType;
#endif

// Send
#if NETSTANDARD2_1
await _client.SendAsync(AsSegment(buffer.Array, 0, buffer.Length),
                        WebSocketMessageType.Text, endOfMessage: true, _cts.Token)
              .ConfigureAwait(false);
#else
await _client.SendAsync(buffer.Array.AsMemory(0, buffer.Length),
                        WebSocketMessageType.Text, endOfMessage: true, _cts.Token)
              .ConfigureAwait(false);
#endif
```

The fast-path UTF-8 decode (`Encoding.UTF8.GetString(ROSpan<byte>)`) and reassembly via `IBufferWriter<byte>` are available on netstandard2.1 (System.Memory is in the netstandard2.1 ref assembly), so only the WebSocket I/O surface is branched. Allocation-wise the netstandard2.1 path costs one extra `Task<WebSocketReceiveResult>` per receive call — acceptable for a tier-2 TFM that real consumers rarely hit, and still strictly better than the current `Websocket.Client` allocation profile.

**This does not affect Unity.** `Reown.Core.Network.WebSocket` is NuGet-only ([[project-websocket-architecture]]); the UPM mirror of `Reown.Core` does not ship its source, and the Unity transport (`WebSocketConnectionUnity`/`NativeWebSocket`) does not use `Channel<T>`. Do **not** add `System.Threading.Channels.dll` to `Reown.Unity.Dependencies` — nothing in the Unity codepath references it.

**Alternative considered:** drop netstandard2.1 from this one package's TFM list. That would eliminate the dependency entirely on the grounds that nobody outside Mono/Xamarin actually picks the netstandard2.1 build of a WebSocket transport. Not done here because (a) it creates an asymmetric TFM matrix vs. `Reown.Core` / `Reown.Sign` / `Reown.WalletKit`, and (b) deciding the future of netstandard2.1 across all Reown packages is a separate RFC, aligned with the broader NativeAOT migration. Revisit when that RFC lands.

**Native AOT.** The design uses no `Reflection.Emit`, no `MakeGenericType` on open generics, no `Activator.CreateInstance` over runtime types, no expression compilation, no `BinaryFormatter`. Mark the project trim/AOT-friendly on every net8+ TFM (NativeAOT support is GA on net8 and forward):

```xml
<PropertyGroup Condition="'$(TargetFramework)' == 'net8.0' Or '$(TargetFramework)' == 'net9.0' Or '$(TargetFramework)' == 'net10.0'">
    <IsAotCompatible>true</IsAotCompatible>
    <IsTrimmable>true</IsTrimmable>
</PropertyGroup>
```

This turns on the trim/AOT analyzers for every modern TFM; CI build failures will catch any future change that breaks AOT compatibility on net8/net9/net10. `IsTrimmable=true` alone is safe to set on net7 as well; `IsAotCompatible` is intentionally **not** set on net7 because the MSBuild property and its analyzer integration were introduced in the net8 SDK — declaring it on net7 silently no-ops and could give a false sense of coverage. Aligns with the broader Reown roadmap of incrementally moving packages to NativeAOT — this is the first one.

**C# language version.** `LangVersion=9.0` is pinned globally for Unity IL2CPP compatibility ([[project-websocket-architecture]] explains why this still applies even though this package is not shipped to Unity). The design uses only C# 9 features: target-typed `new`, init-only setters, pattern matching, `record` (for the small `PooledSendBuffer` struct if desired — though a regular `readonly struct` is preferable). No file-scoped namespaces, no global usings, no collection expressions, no required members.

### 2.5 Allocation budget (steady state, projected)

The numbers below are *projected* and need to be confirmed by §4.6's measurement test. For a connection that's been open for a while (pool warm) and is exchanging small JSON-RPC messages:

| Path | Allocations per message (projected) |
|---|---|
| Receive (single frame, fast path) | 1 — the `string` raised in `PayloadReceived` (unavoidable; consumer expects a string). |
| Receive (fragmented) | 1 — same string. Reassembly buffer comes from `ArrayPool` (amortized zero once warm). |
| Send | 0 amortized — JSON string is supplied by caller; the byte buffer is rented from `ArrayPool` (amortized zero once warm; first-time rent and growth on capacity bump still allocate). `Channel<T>` may allocate internal nodes per write; the bounded-channel implementation amortises this. |
| Close | bounded by the disconnect-info object that the `Closed` event currently carries. |

Compared to today's `Websocket.Client` path: the library allocates `RecyclableMemoryStream` segments per receive (pooled, but pooling is its own object), constructs `ResponseMessage`/`DisconnectionInfo` for the Rx observables, and re-encodes the outgoing string to UTF-8 inside its send loop. Net allocation should drop, though the magnitude depends on payload size and traffic pattern — the §4.6 benchmark is the place to actually measure it.

### 2.6 Connection lifecycle

```
Open()  → new ClientWebSocketTransport(url)
        → transport.StartAsync(token)
            ↳ using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            ↳ connectCts.CancelAfter(OpenTimeout);  // 60 s, see §2.8
            ↳ try ClientWebSocket.ConnectAsync(uri, connectCts.Token);
              catch OperationCanceledException when CancelAfter fired →
                  _client.Abort(); _client.Dispose();
                  throw new TimeoutException($"WS connect to {Url} exceeded {OpenTimeout}");
            ↳ start receive + send loops
            ↳ raise Opened

Close() → transport.StopAsync(WebSocketCloseStatus.NormalClosure, "Close Invoked")
            ↳ _shutdownRequested = true             // gate for receive/send loop catch arms
            ↳ outbox.Writer.Complete()              // stop accepting new sends
            ↳ await _sendLoop (bounded 2s)          // drain in-flight + queued sends
            ↳ ClientWebSocket.CloseOutputAsync(...) // exclusive send-side, safe after sender stops
            ↳ await receive loop, bounded 3s        // server should reply with Close frame
            ↳ cancel _cts                           // forces any remaining awaits to unblock
            ↳ await receive + send loops to terminal state (already cancelled)
            ↳ raise Closed
```

**Send-side serialization is required.** `ClientWebSocket` allows exactly one outstanding send-side operation (`SendAsync` *or* `CloseOutputAsync`) at a time — concurrent send-side calls throw `InvalidOperationException`. The outbox channel guarantees only the send-loop calls `SendAsync`, so `CloseOutputAsync` must be issued **after** the send-loop has terminated, not in parallel with it. The order above (complete writer → await send-loop drain → `CloseOutputAsync`) preserves that invariant while still flushing queued messages.

**What "await `_sendLoop` (bounded 2 s)" means concretely.** `_sendLoop` is a `Task` representing the entire consumer loop. Completing `outbox.Writer` stops new items from being enqueued, but an in-flight `SendAsync` continues until the network write returns. The 2 s budget is on the *Task*, not the socket call: `await Task.WhenAny(_sendLoop, Task.Delay(2_000))`. If the budget expires, cancel `_cts` (which only cancels future awaits — it does **not** abort the active `SendAsync` mid-write) and proceed to `Dispose` the underlying socket, which severs TCP and forces the in-flight write to fault. This means `CloseOutputAsync` is guaranteed not to overlap an in-flight `SendAsync` because either the loop's task completed (sender finished naturally) or we skipped graceful close entirely.

`CloseAsync` (which combines `CloseOutputAsync` + waits for the server's close frame) is **not** used because it internally calls `ReceiveAsync`, which would race the running receive loop. Manual `CloseOutputAsync` + waiting on our own receive loop is the correct pattern.

`Open()` failures translate to the same `IOException("Unavailable WS RPC url at " + Url)` the existing code raises for `getaddrinfo ENOTFOUND` / `ECONNREFUSED` (see `WebsocketConnection.cs:316-321`). Map `WebSocketException` and `SocketException` (inner) by inspecting `SocketError`:

- `SocketError.HostNotFound`, `SocketError.NoData` → `IOException("Unavailable WS RPC url at " + Url)`
- `SocketError.ConnectionRefused`, `SocketError.HostUnreachable` → same.
- Otherwise → rethrow as-is so the caller can surface the original exception.

The existing string-match on `"getaddrinfo ENOTFOUND"` / `"connect ECONNREFUSED"` can be removed — those messages came from Node-style errors that surface from `Websocket.Client` on some platforms but are not produced by `ClientWebSocket`.

### 2.7 `Connected` / state mapping

```csharp
public bool Connected => _transport is { State: WebSocketState.Open };
```

`ClientWebSocket.State` exposes the same `WebSocketState` enum used today (`WebsocketConnection.cs:70`), so the mapping is 1:1.

### 2.8 Configuration knobs

`ClientWebSocket.Options` exposes a few settings worth wiring through `WebsocketConnection`:

| Option | Default | Reason |
|---|---|---|
| `KeepAliveInterval` | 30 s | Match `Websocket.Client` default. Important for relay (corporate NATs and mobile carrier NATs drop idle TCP after ~60 s). Available on all five TFMs. |
| `HttpVersion` | `1.1` (net7+ only) | Don't enable HTTP/2 over WS; relay doesn't require it and `RFC 8441` support is uneven. **`ClientWebSocketOptions.HttpVersion` is net7+ only; the entire line is wrapped in `#if !NETSTANDARD2_1`.** On netstandard2.1 `ClientWebSocket` defaults to HTTP/1.1 anyway, so omitting the assignment is observably identical. |
| Sub-protocol | none | Relay doesn't negotiate a sub-protocol. |
| Headers | none | None used today. |

**Open timeout (60 s, matches `WebsocketConnection.cs:54-57`).** Do **not** use the existing `Extensions.WithTimeout` wrapper for `ConnectAsync`. That helper races a `Task.Delay`: when the delay wins it throws `TimeoutException` to the caller but leaves the in-flight `ConnectAsync` running on a detached task, holding the socket, the TLS handshake state, and the response stream. For a server that completes the TCP handshake but stalls the WebSocket upgrade (proxy soft-fail, half-open connection), this leaks a transport per failed `Open()`.

Use a linked `CancellationTokenSource` instead:

```csharp
using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
connectCts.CancelAfter(OpenTimeout);            // 60 s
try
{
    await _client.ConnectAsync(uri, connectCts.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) when (!externalToken.IsCancellationRequested
                                         && connectCts.IsCancellationRequested)
{
    // CancelAfter fired: dispose the socket so the TCP/TLS resources are released,
    // then surface a timeout to the caller (same observable shape as today's WithTimeout path).
    _client.Abort();
    _client.Dispose();
    throw new TimeoutException($"WebSocket connect to {Url} exceeded {OpenTimeout}.");
}
```

`ClientWebSocket.ConnectAsync` honours cancellation by aborting the underlying HTTP request (and the socket) before its task transitions to `Canceled`, so this path is leak-free. The same pattern applies if we ever add a `CancellationToken` overload on `Open()` — the external token gets linked in for free. `WithTimeout` is still appropriate for `_sendLoop`/`_receiveLoop` shutdown waits, where the underlying task already observes `_cts` and we just need a wall-clock budget on `await Task.WhenAny`.

### 2.9 Removed dependencies

After migration, `Reown.Core.Network.WebSocket.csproj` has only one `PackageReference` left — the conditional `System.Threading.Channels` needed for the netstandard2.1 build (see §2.4):

```xml
<ItemGroup>
  <ProjectReference Include="..\Reown.Core.Common\Reown.Core.Common.csproj"/>
  <ProjectReference Include="..\Reown.Core.Network\Reown.Core.Network.csproj"/>
</ItemGroup>

<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.1'">
  <PackageReference Include="System.Threading.Channels"/>
</ItemGroup>
```

**Removed:** `Websocket.Client`. **Transitively removed:** `System.Reactive`, `Microsoft.IO.RecyclableMemoryStream` (the `Microsoft.Extensions.Logging.Abstractions` edge is still pulled in by other Reown packages and stays in the closure).

**Added:** `System.Threading.Channels` (netstandard2.1 TFM only; runtime-inbox on net7/net8/net9/net10 so no transitive cost on modern frameworks).

`Directory.Packages.props` loses the `Websocket.Client` entry and gains `System.Threading.Channels`.

---

## 3. Step-by-step plan

1. **Branch:** `chore/replace-websocket-client` off `develop`.
2. Add a new internal class `ClientWebSocketTransport` in `src/Reown.Core.Network.WebSocket/Internal/ClientWebSocketTransport.cs`.
3. Rewrite `WebsocketConnection` to hold a `ClientWebSocketTransport` instead of `WebsocketClient`. Keep all public members and event names identical. Remove `using Websocket.Client;`.
4. Remove `<PackageReference Include="Websocket.Client"/>` from the csproj and the corresponding entry in `Directory.Packages.props`.
5. Run `dotnet restore` and confirm no stale references in `packages.lock.json` files across the solution (none today, but verify).
6. Add unit tests (section 4).
7. Run the full `dotnet-build-test.yml` flow locally on `net8`, `net9`, `net10`.
8. Manually verify the sample app's relay round-trip on Windows desktop, macOS desktop, Android device, iOS device. WebGL is unaffected.
9. Tag and ship in a minor (e.g. `1.6.0`).

### Rollback

Single-file rollback: revert the commit that swaps out `WebsocketConnection`'s internals and re-add the `PackageReference`. Public API is unchanged, so downstream packages don't move.

---

## 4. Test plan

The existing `Reown.Core.Network.Test/RelayTests.cs` is an integration-only suite that requires `PROJECT_ID`. It exercises the *happy path* against a live relay. That is necessary but not sufficient — the migration touches receive-loop fragmentation, send serialisation, and shutdown ordering, none of which the happy-path test covers.

The plan below adds (a) an in-process WebSocket server fixture so we can exercise pathological server behaviour deterministically, and (b) a property-style test for fragmented payload reassembly that's robust under CI parallelism.

### 4.1 New test infrastructure

Create `test/Reown.Core.Network.Test/Fixtures/InProcessWebSocketServer.cs`. **Note**: `HttpListener` registers `http://` prefixes (not `ws://`) and rejects port `0` in prefix strings, so the fixture has to discover an ephemeral port first, then bind:

1. `var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();`
2. `int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();`
3. `var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();`
4. For each `HttpListenerContext`, `await context.AcceptWebSocketAsync(null)` to perform the HTTP→WS upgrade.
5. Hand the test client the URL `ws://127.0.0.1:{port}/` — `ClientWebSocket` accepts the `ws` scheme and the listener responds because the host:port matches.

This has a small TOCTOU window between `probe.Stop()` and `listener.Start()` where another process could grab the port; that's acceptable for CI but tests should retry once on `HttpListenerException`/`SocketException` at startup. A lower-level alternative (raw `TcpListener` + hand-rolled RFC 6455 handshake) avoids the dance but adds ~150 LOC of frame parsing — not worth it when `HttpListener` does the upgrade for free.

The fixture exposes:

- Async hooks: `OnClientConnected`, `OnTextMessage`.
- Controls: `SendTextAsync(string)`, `SendFragmentedTextAsync(IEnumerable<string>)`, `SendBinaryAsync(byte[])`, `CloseAsync(WebSocketCloseStatus, string)`, `AbortAsync()` (closes the underlying `TcpClient` without sending a WS close frame — TCP RST, used for the abrupt-disconnect tests).
- `IDisposable`; releases the port and joins server task on dispose.

This isolates the unit tests from the live relay and makes the deterministic-failure cases below possible.

### 4.2 Test cases (xUnit, `Category=unit`)

| # | Test | Asserts |
|---|---|---|
| **Connect** | | |
| 1 | `Open_succeeds_against_local_server` | `Connected == true`, `Opened` event fired once. |
| 2 | `Open_throws_IOException_on_connection_refused` | Connects to a closed port → `IOException` with `"Unavailable WS RPC url"` message. |
| 3 | `Open_throws_IOException_on_unresolvable_host` | `ws://does-not-resolve.example/`. |
| 4 | `Open_respects_OpenTimeout` | Server accepts TCP but never completes the HTTP upgrade → `Open()` faults with `TimeoutException` within `OpenTimeout + slack`. Assert no `ClientWebSocket` instance is left alive afterwards (`_client.State == Aborted`/`Closed`), proving the underlying `ConnectAsync` was cancelled and the socket disposed rather than detached. |
| 5 | `Open_with_invalid_url_throws_ArgumentException` | Mirrors current behaviour at `WebsocketConnection.cs:36`. |
| 6 | `Open_concurrent_callers_share_one_connection` | Two `Open()` calls in parallel produce one `Opened` event and one socket; both await the same task. Mirrors current `Register` re-entrancy (`WebsocketConnection.cs:210-220`). |
| **Send** | | |
| 7 | `SendRequest_round_trip` | Server echoes; `PayloadReceived` fires with the same payload. |
| 8 | `Send_before_open_implicitly_opens` | `_socket ??= await Register(Url)` path (`WebsocketConnection.cs:124`). |
| 9 | `Send_from_many_threads_preserves_frame_integrity` | 200 concurrent `SendRequest` calls fanned across 8 threads. Server reassembles each frame and asserts every payload arrives intact (no interleaving across frames). Use `[Fact(Timeout=30_000)]` so a stalled CI worker fails fast instead of hanging the suite. This is the key safety property the `Channel`-backed send loop must hold; smaller fan-out keeps the determinism while removing CI flake risk. |
| 10 | `Send_after_Close_implicitly_reopens` | After `Close()`, the next `SendRequest` re-enters `_socket ??= await Register(Url)` and opens a fresh connection. This matches today's behaviour at `WebsocketConnection.cs:124` and is one of the contract invariants the migration must preserve. The test asserts `Opened` fires a second time and the send succeeds end-to-end. |
| 10b | `Send_when_open_failed_raises_error_via_PayloadReceived` | If the underlying `SendAsync` throws (server abort mid-send), the in-flight payload is converted to a JSON-RPC error and dispatched via `PayloadReceived`, matching `OnError` behaviour at `WebsocketConnection.cs:316-333`. This is the genuine error-path test that the original test 10 was trying to capture. |
| **Receive** | | |
| 11 | `Receives_single_text_frame` | Server sends 100-byte text → `PayloadReceived` fires with exact payload. |
| 12 | `Receives_fragmented_text_frame` | Server sends one logical message in 3 fragments (`endOfMessage=false, false, true`) → exactly one `PayloadReceived` with reassembled UTF-8 string. |
| 13 | `Receives_payload_larger_than_buffer` | Server sends 64 KiB JSON in one logical message → one `PayloadReceived`, exact byte-for-byte match. Catches off-by-one in the `PooledByteBufferWriter` grow/flush path. |
| 14 | `Receives_multibyte_utf8_at_fragment_boundary` | Server splits a multi-byte UTF-8 sequence (e.g. `é` = `0xC3 0xA9`) across two fragments. Asserts (a) the reassembled string equals the original byte-for-byte after decode, and (b) the result contains no `U+FFFD` replacement characters. The latter is the load-bearing assertion: a naive per-fragment `Encoding.UTF8.GetString` call would produce `U+FFFD` at the boundary, and exact-equality alone won't catch that if the test payload happens to mismatch in other ways. |
| 15 | `Ignores_binary_frames` | Server sends a binary frame → `PayloadReceived` not invoked. Matches `WebsocketConnection.cs:281`. |
| 16 | `Empty_text_frame_does_not_raise` | Server sends `""` → no `PayloadReceived`. Matches `WebsocketConnection.cs:290-291`. |
| **Close** | | |
| 17 | `Server_initiated_close_raises_Closed` | Server sends `CloseAsync(NormalClosure)` → `Closed` event fires once, `Connected == false`. |
| 18 | `Client_Close_completes_within_5s_against_unresponsive_server` | Server stops reading but doesn't send close. `Close()` cancels the wait and returns; receive/send loops join cleanly. Guards against the SignalR-style hang. **Also asserts `ErrorReceived` never fires during shutdown** — the receive/send loops' `OperationCanceledException` must be classified as intentional via the `_shutdownRequested` gate (§2.2). |
| 19 | `Abrupt_tcp_reset_raises_ErrorReceived_then_Closed` | Server `AbortAsync()` → `ErrorReceived` (with `WebSocketException`/`IOException`) and `Closed` both fire. Asserts exactly one `Closed` and that `ErrorReceived` precedes `Closed`. |
| 20 | `Close_when_already_closed_throws_IOException` | Mirrors `WebsocketConnection.cs:107-108`. |
| 21 | `Dispose_after_open_releases_resources` | After `Dispose()`, attempting `SendRequest` throws/no-ops; receive task is faulted/cancelled and joined; no leaked sockets (check via `lsof` count in a sanity probe). |
| 22 | `Dispose_is_idempotent` | Calling `Dispose()` twice does not throw. |
| **Cancellation & timeouts** | | |
| 23 | `Open_cancellation_propagates_through_ConnectAsync` | (If we add a CancellationToken overload) — verify the underlying `ConnectAsync` is cancelled and no socket lingers. |
| 24 | `KeepAliveInterval_is_configured_nonzero` | Construct the transport and assert (via internal-visible accessor) that `ClientWebSocket.Options.KeepAliveInterval > TimeSpan.Zero`. We can't observe pong frames directly — `HttpListener`/`System.Net.WebSockets` handles ping/pong below the `ReceiveAsync` surface. |
| 24b | `Idle_connection_survives_keepalive_window` | Construct the transport with `KeepAliveInterval = 2 s`, keep the connection open for ~6 s without app-level traffic, then send and verify round trip. Proves keepalive runs end-to-end. Total runtime ~10 s; avoids the 90 s × 3-TFM CI cost while still catching a regression where keepalive is disabled and the server times out the idle connection. Gate behind an env var (`REOWN_RUN_LONG_TESTS=1`) if it proves flaky on shared runners. |
| **Concurrency / fuzz** | | |
| 25 | `Receive_loop_handles_interleaved_small_frames` | Server sends 10 000 small (~50 byte) text frames as fast as possible; client raises exactly 10 000 `PayloadReceived` events with matching contents. Run with `Repeat(5)` to flush out races. |
| 26 | `Connection_lifecycle_under_torn_open_close` | 50× `(Open → SendRequest → Close)`; no event leakage between iterations (each instance fires exactly one `Opened` and one `Closed`). |

### 4.3 Existing integration tests

Keep `RelayTests.cs` as-is. After migration, run the suite against the live relay on Windows + Linux + macOS runners using `Category=integration`. This is the smoke test for real-world TLS, NAT, and relay framing.

### 4.4 Cross-target matrix

The CI matrix should run unit tests on `net8.0`, `net9.0`, and `net10.0`. The existing `.github/workflows/dotnet-build-test.yml` matrix already covers all three TFMs after the net9/net10 bump; the *unit* suite for `Reown.Core.Network.Test` needs to run across all of them because `ClientWebSocket` has had small behavioural changes (e.g. `WebSocketCreationOptions` introduced in net7, the `ConnectAsync(Uri, HttpMessageInvoker, CancellationToken)` overload in net7, header behaviour tweaks in net8). The current `<TargetFrameworks>$(DefaultTargetFrameworks)</TargetFrameworks>` already covers this; just ensure CI invokes `dotnet test` with `-f net8.0`, `-f net9.0`, `-f net10.0` separately or relies on the multi-target run. Integration tests may run on a narrower TFM set if relay throughput becomes a constraint — that's a CI-tuning decision, not a correctness one.

### 4.5 Manual verification before release

- WalletKit sample (NuGet consumer, non-Unity) does a session pair + respond round trip on .NET 8 / 9 / 10.
- Run `dotnet test Reown.NoUnity.slnf --filter Category=integration` against the live relay on Windows + Linux + macOS runners.
- **Unity is intentionally out of scope** — it uses `WebSocketConnectionUnity` / vendored `NativeWebSocket`, not this package. The Unity sample, AppKit, Android/iOS/WebGL builds do not need re-verification for this migration.

### 4.6 Allocation assertion (unit test, not just benchmark)

Add one xUnit test in `Category=unit` that uses `GC.GetTotalAllocatedBytes(precise: true)` (net5+) to assert the steady-state send path allocates within a budget:

```
warm up the connection (one full round trip + force a Gen2 GC),
var before = GC.GetTotalAllocatedBytes(precise: true);
SendRequest × 1000 with a fixed 200-byte JSON payload, awaiting each round trip,
var after  = GC.GetTotalAllocatedBytes(precise: true);
assert (after - before) < N
```

`GC.GetTotalAllocatedBytes` measures process-wide allocations, so it captures the send-loop, receive-loop, and any continuation thread — `GC.GetAllocatedBytesForCurrentThread()` is the wrong API here because both loops run on thread-pool threads and their allocations would be invisible to the test thread.

To reduce noise, run on a single-threaded `SynchronizationContext` if possible and call `GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();` before the `before` snapshot. Size `N` to allow one string per inbound echo (~1000 strings × 200 chars × 2 bytes ≈ 400 KiB plus header overhead) plus reasonable Channel bookkeeping (~100 KiB), but block a regression where someone reintroduces a per-message `byte[]` allocation on the send path (which would add ~200 KiB+ at 1000 iterations and blow the budget). This is the cheapest way to make "we use ArrayPool" load-bearing instead of aspirational.

### 4.7 Performance regression check

Add a `BenchmarkDotNet` micro-benchmark (in a new `bench/` project, excluded from normal CI) measuring:

- Single text-message round trip latency (median, p99) against in-process server.
- Throughput at 1 KB messages, sustained 10 s.
- Allocations per message (`MemoryDiagnoser`).

Run once before merging and attach numbers to the PR. We do not expect a regression — the new code path is shorter — but the data is cheap and makes the migration auditable. No CI gate; this is a one-off measurement.

---

## 5. Risks & open questions

| Risk | Mitigation |
|---|---|
| Edge cases in fragment reassembly | Tests 12–14; port SignalR's idiom verbatim rather than improvising. |
| Stop()/Close() hang on unresponsive server | Test 18; cap the post-close drain at 5 s; cancel the receive token. |
| Behavioural diff in error messages for unresolvable hosts | Map `SocketException` explicitly (see §2.4); test 3 asserts the exact `IOException` message contract. |
| Net9/net10 `ClientWebSocket` quirks | Multi-TFM unit test matrix (§4.4). |
| Hidden consumer of `Websocket.Client` types | `grep -rn "Websocket.Client\|WebsocketClient\b" src/ test/` returns only this package today (verified). After removal, the dependency is fully gone from the closure. |
| `WebsocketConnection` is `public` and could be subclassed externally | Its constructor and event surface are unchanged, so external subclasses still compile. The internal field type changes from `WebsocketClient` to a private/internal transport — no public type change. |

### Open questions

1. Should we expose `KeepAliveInterval` and `OpenTimeout` as constructor parameters in this migration? *Recommendation: no — keep the public surface frozen for this PR, file a follow-up if needed.*
2. Should we add a `CancellationToken` overload on `Open()`/`Close()`? *Recommendation: no — follow-up.*
3. Should the in-process server fixture live in `Rown.TestUtils` for reuse by `Reown.Sign.Test`? *Recommendation: yes, but as a separate PR after this one lands and the API has settled.*

---

## 6. References

- Current implementation: `src/Reown.Core.Network.WebSocket/WebsocketConnection.cs`
- SignalR transport (canonical pattern):
  `https://github.com/dotnet/aspnetcore/blob/main/src/SignalR/clients/csharp/Http.Connections.Client/src/Internal/WebSocketsTransport.cs`
- MQTTnet WS channel (alternative reference, no Rx):
  `https://github.com/dotnet/MQTTnet`
- `Websocket.Client` upstream: `https://github.com/Marfusios/websocket-client`
- Relevant open issues in upstream (informational): #139 (Stop deadlock), #148 (lost message after reconnect), #154 (ObjectDisposedException).
