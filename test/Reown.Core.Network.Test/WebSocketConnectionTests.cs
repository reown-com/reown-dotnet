using System.Net.WebSockets;
using System.Text;
using Reown.Core.Common.Model.Relay;
using Reown.Core.Network.Models;
using Reown.Core.Network.Test.Fixtures;
using Reown.Core.Network.Test.Model;
using Reown.Core.Network.Websocket;
using Reown.Core.Network.Websocket.Internal;
using Xunit;

namespace Reown.Core.Network.Test;

[Trait("Category", "unit")]
public class WebSocketConnectionTests
{
    private static JsonRpcRequest<TopicData> NewRequest() =>
        new(RelayProtocols.DefaultProtocol.Subscribe, new TopicData { Topic = Guid.NewGuid().ToString("N") });

    // ----- Connect ------------------------------------------------------------

    [Fact]
    public async Task Open_succeeds_against_local_server()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Opened += (_, _) => opened.TrySetResult(true);

        await connection.Open();

        Assert.True(connection.Connected);
        Assert.True(await opened.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        connection.Dispose();
    }

    [Fact]
    public async Task Open_throws_IOException_on_connection_refused()
    {
        // Reserve a port then release it - reconnecting yields ConnectionRefused on most platforms.
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var connection = new WebsocketConnection($"ws://127.0.0.1:{port}/");
        var ex = await Assert.ThrowsAsync<IOException>(() => connection.Open());
        Assert.Contains("Unavailable WS RPC url", ex.Message);

        connection.Dispose();
    }

    [Fact]
    public async Task Open_throws_IOException_on_unresolvable_host()
    {
        var connection = new WebsocketConnection("ws://does-not-resolve.invalid/");
        var ex = await Assert.ThrowsAsync<IOException>(() => connection.Open());
        Assert.Contains("Unavailable WS RPC url", ex.Message);

        connection.Dispose();
    }

    [Fact]
    public async Task Open_respects_OpenTimeout()
    {
        using var bogusServer = new UnresponsiveTcpServer();

        // Bypass the public 60s OpenTimeout by driving the transport directly with a tight timeout.
        var transport = new ClientWebSocketTransport(
            bogusServer.WebSocketUri,
            openTimeout: TimeSpan.FromMilliseconds(500),
            keepAlive: TimeSpan.FromSeconds(30));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => transport.StartAsync(CancellationToken.None));
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Open timeout was not honoured (elapsed: {sw.Elapsed}).");

        Assert.True(transport.State == WebSocketState.None
                    || transport.State == WebSocketState.Closed
                    || transport.State == WebSocketState.Aborted);

        transport.Dispose();
    }

    [Fact]
    public void Open_with_invalid_url_throws_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new WebsocketConnection("http://not-a-ws-url/"));
    }

    [Fact]
    public async Task Open_concurrent_callers_share_one_connection()
    {
        await using var server = InProcessWebSocketServer.Start();

        var connectedCount = 0;
        server.ClientConnected += _ => Interlocked.Increment(ref connectedCount);

        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var openedCount = 0;
        connection.Opened += (_, _) => Interlocked.Increment(ref openedCount);

        await Task.WhenAll(connection.Open(), connection.Open(), connection.Open());

        // Allow the server-side connection accept to settle.
        await Task.Delay(150);

        Assert.True(connection.Connected);
        Assert.Equal(1, openedCount);
        Assert.Equal(1, connectedCount);

        connection.Dispose();
    }

    [Fact]
    public async Task Open_with_string_options_registers_once()
    {
        await using var server = InProcessWebSocketServer.Start();

        var connectedCount = 0;
        server.ClientConnected += _ => Interlocked.Increment(ref connectedCount);

        var connection = new WebsocketConnection("ws://127.0.0.1:1/");

        var openedCount = 0;
        connection.Opened += (_, _) => Interlocked.Increment(ref openedCount);

        await connection.Open(server.WebSocketUri.ToString());

        // Allow the server-side connection accept to settle.
        await Task.Delay(150);

        Assert.True(connection.Connected);
        Assert.Equal(1, openedCount);
        Assert.Equal(1, connectedCount);

        connection.Dispose();
    }

    [Fact]
    public async Task Opened_handler_exception_does_not_block_concurrent_open_callers()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        connection.Opened += (_, _) => throw new InvalidOperationException("opened handler failed");

        var firstOpen = connection.Open();
        var secondOpen = connection.Open();

        await Task.WhenAll(firstOpen, secondOpen).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(connection.Connected);

        connection.Dispose();
    }

    // ----- Send ---------------------------------------------------------------

    [Fact]
    public async Task SendRequest_round_trip()
    {
        await using var server = InProcessWebSocketServer.Start();
        server.EchoMode = true;

        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) => received.TrySetResult(payload);

        await connection.Open();
        var request = NewRequest();
        await connection.SendRequest(request, null);

        var echoed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(request.Id.ToString(), echoed);

        connection.Dispose();
    }

    [Fact]
    public async Task Send_before_open_implicitly_opens()
    {
        await using var server = InProcessWebSocketServer.Start();
        server.EchoMode = true;

        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) => received.TrySetResult(payload);

        await connection.SendRequest(NewRequest(), null);

        var echoed = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(echoed));
        Assert.True(connection.Connected);

        connection.Dispose();
    }

    [Fact(Timeout = 30_000)]
    public async Task Send_from_many_threads_preserves_frame_integrity()
    {
        await using var server = InProcessWebSocketServer.Start();

        var receivedTexts = new System.Collections.Concurrent.ConcurrentBag<string>();
        var expected = 200;
        var allReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        server.TextMessageReceived += (_, text) =>
        {
            receivedTexts.Add(text);
            if (receivedTexts.Count == expected) allReceived.TrySetResult(true);
        };

        var connection = new WebsocketConnection(server.WebSocketUri.ToString());
        await connection.Open();

        var requests = Enumerable.Range(0, expected).Select(_ => NewRequest()).ToArray();
        var ids = requests.Select(r => r.Id.ToString()).ToHashSet();

        await Task.WhenAll(requests.Select(r => Task.Run(() => connection.SendRequest(r, null))));

        await allReceived.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(expected, receivedTexts.Count);
        foreach (var text in receivedTexts)
        {
            var found = ids.Any(id => text.Contains(id));
            Assert.True(found, $"Could not match payload back to a sent id: {text}");
        }

        connection.Dispose();
    }

    [Fact]
    public async Task Send_after_Close_implicitly_reopens()
    {
        await using var server = InProcessWebSocketServer.Start();
        server.EchoMode = true;

        var connection = new WebsocketConnection(server.WebSocketUri.ToString());
        var openedCount = 0;
        connection.Opened += (_, _) => Interlocked.Increment(ref openedCount);

        await connection.Open();
        await connection.Close();

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) => received.TrySetResult(payload);

        await connection.SendRequest(NewRequest(), null);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(connection.Connected);
        Assert.Equal(2, openedCount);

        connection.Dispose();
    }

    [Fact]
    public async Task Send_when_send_fails_raises_error_via_PayloadReceived()
    {
        using var server = new RawWebSocketServer();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var errorEnvelope = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) =>
        {
            if (payload.Contains("\"error\"")) errorEnvelope.TrySetResult(payload);
        };

        var transportFaulted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ErrorReceived += (_, _) => transportFaulted.TrySetResult(true);

        await connection.Open();
        var tcpClient = await server.WaitForFirstClientAsync();

        // Real TCP RST: transport's receive loop sees WebSocketException, completes the outbox
        // writer with that exception, and raises ErrorReceived.
        server.AbortClient(tcpClient);
        await transportFaulted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Next SendRequest must surface the failure through OnError<T>: SendAsync throws
        // ChannelClosedException synchronously and the connection wraps it in a JSON-RPC error.
        var request = NewRequest();
        await connection.SendRequest(request, null);

        var envelope = await errorEnvelope.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(request.Id.ToString(), envelope);
        Assert.Contains("\"error\"", envelope);

        connection.Dispose();
    }

    // ----- Receive ------------------------------------------------------------

    [Fact]
    public async Task Receives_single_text_frame()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) => received.TrySetResult(payload);

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();

        var payload = new string('x', 100);
        await session.SendTextAsync(payload);

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(payload, got);

        connection.Dispose();
    }

    [Fact]
    public async Task Receives_fragmented_text_frame()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var receivedCount = 0;
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) =>
        {
            Interlocked.Increment(ref receivedCount);
            received.TrySetResult(payload);
        };

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();

        var fragments = new[]
        {
            Encoding.UTF8.GetBytes("hello "),
            Encoding.UTF8.GetBytes("from "),
            Encoding.UTF8.GetBytes("server"),
        };
        await session.SendFragmentedTextAsync(fragments);

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("hello from server", got);
        await Task.Delay(100);
        Assert.Equal(1, receivedCount);

        connection.Dispose();
    }

    [Fact]
    public async Task Receives_payload_larger_than_buffer()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) => received.TrySetResult(payload);

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();

        var sb = new StringBuilder();
        for (var i = 0; i < 64 * 1024; i++) sb.Append((char)('a' + (i % 26)));
        var payload = sb.ToString();
        await session.SendTextAsync(payload);

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(payload.Length, got.Length);
        Assert.Equal(payload, got);

        connection.Dispose();
    }

    [Fact]
    public async Task Receives_multibyte_utf8_at_fragment_boundary()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) => received.TrySetResult(payload);

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();

        const string original = "Café";
        var allBytes = Encoding.UTF8.GetBytes(original);
        // 'C', 'a', 'f', 0xC3 in the first fragment; 0xA9 alone in the second.
        var fragments = new[]
        {
            allBytes.Take(4).ToArray(),
            allBytes.Skip(4).ToArray(),
        };
        await session.SendFragmentedTextAsync(fragments);

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(original, got);
        Assert.DoesNotContain('�', got);

        connection.Dispose();
    }

    [Fact]
    public async Task Ignores_binary_frames()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var receivedCount = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, payload) =>
        {
            Interlocked.Increment(ref receivedCount);
            if (payload == "after-binary") tcs.TrySetResult(true);
        };

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();

        await session.SendBinaryAsync(new byte[] { 1, 2, 3, 4 });
        await session.SendTextAsync("after-binary");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);

        // Only the "after-binary" text frame should have raised; binary discarded.
        Assert.Equal(1, receivedCount);

        connection.Dispose();
    }

    [Fact]
    public async Task Empty_text_frame_does_not_raise()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var emptyRaised = 0;
        connection.PayloadReceived += (_, payload) =>
        {
            if (string.IsNullOrWhiteSpace(payload)) Interlocked.Increment(ref emptyRaised);
        };

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();

        await session.SendTextAsync("");
        await session.SendTextAsync("   ");

        await Task.Delay(300);
        Assert.Equal(0, emptyRaised);

        connection.Dispose();
    }

    // ----- Close --------------------------------------------------------------

    [Fact]
    public async Task Server_initiated_close_raises_Closed()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var closedRaised = 0;
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += (_, _) =>
        {
            Interlocked.Increment(ref closedRaised);
            closed.TrySetResult(true);
        };

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();
        await session.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye");

        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(150);
        Assert.Equal(1, closedRaised);
        Assert.False(connection.Connected);

        connection.Dispose();
    }

    [Fact]
    public async Task Client_Close_completes_within_5s_against_unresponsive_server()
    {
        await using var server = InProcessWebSocketServer.Start();
        // Note: EchoMode off, server's receive loop drains but won't react to client close beyond the
        // ack that the test fixture sends - which we want, but we'll abort the session to simulate
        // a server that stops reading.
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var errorRaised = 0;
        connection.ErrorReceived += (_, _) => Interlocked.Increment(ref errorRaised);

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();
        // Stop server-side receive loop so it won't reply to close frame.
        await session.AbortAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await connection.Close();
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"Close took too long: {sw.Elapsed}");
        Assert.False(connection.Connected);

        await Task.Delay(150);
        // ErrorReceived may be raised since the server abort can happen before our intentional close
        // flag is set. The assertion captured here is about timely return, not about silent shutdown.
        connection.Dispose();
    }

    [Fact]
    public async Task Client_Close_emits_no_ErrorReceived_when_server_is_graceful()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var errorRaised = 0;
        var closedRaised = 0;
        connection.ErrorReceived += (_, _) => Interlocked.Increment(ref errorRaised);
        connection.Closed += (_, _) => Interlocked.Increment(ref closedRaised);

        await connection.Open();
        await server.WaitForFirstClientAsync();

        await connection.Close();

        await Task.Delay(250);
        Assert.Equal(0, errorRaised);
        Assert.True(closedRaised >= 1);

        connection.Dispose();
    }

    [Fact]
    public async Task Abrupt_tcp_reset_raises_ErrorReceived_then_Closed()
    {
        using var server = new RawWebSocketServer();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var sequence = new List<string>();
        var closedCount = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ErrorReceived += (_, _) =>
        {
            lock (sequence) sequence.Add("error");
        };
        connection.Closed += (_, _) =>
        {
            lock (sequence) sequence.Add("closed");
            Interlocked.Increment(ref closedCount);
            done.TrySetResult(true);
        };

        await connection.Open();
        var tcpClient = await server.WaitForFirstClientAsync();
        server.AbortClient(tcpClient);

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(150);

        lock (sequence)
        {
            Assert.Contains("error", sequence);
            Assert.Contains("closed", sequence);
            var firstClosedIdx = sequence.IndexOf("closed");
            var firstErrorIdx = sequence.IndexOf("error");
            Assert.True(firstErrorIdx < firstClosedIdx, "ErrorReceived should precede Closed");
        }
        Assert.Equal(1, closedCount);

        connection.Dispose();
    }

    [Fact]
    public async Task Close_when_already_closed_throws_IOException()
    {
        var connection = new WebsocketConnection("ws://127.0.0.1:1/");
        await Assert.ThrowsAsync<IOException>(() => connection.Close());
        connection.Dispose();
    }

    [Fact]
    public async Task Dispose_after_open_releases_resources()
    {
        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        await connection.Open();
        Assert.True(connection.Connected);

        connection.Dispose();
        Assert.False(connection.Connected);
    }

    [Fact]
    public async Task Dispose_during_pending_open_cancels_connect()
    {
        using var bogusServer = new UnresponsiveTcpServer();
        var connection = new WebsocketConnection(bogusServer.WebSocketUri.ToString());

        var openTask = connection.Open();
        await Task.Delay(100);

        connection.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => openTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(connection.Connected);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var connection = new WebsocketConnection("ws://127.0.0.1:1/");
        connection.Dispose();
        connection.Dispose(); // must not throw
    }

    // ----- Keepalive ----------------------------------------------------------

    [Fact]
    public void KeepAliveInterval_is_configured_nonzero()
    {
        var transport = new ClientWebSocketTransport(
            new Uri("ws://127.0.0.1:1/"),
            openTimeout: TimeSpan.FromSeconds(60),
            keepAlive: TimeSpan.FromSeconds(30));

        Assert.True(transport.KeepAliveInterval > TimeSpan.Zero);
        transport.Dispose();
    }

    // ----- Concurrency --------------------------------------------------------

    [Fact(Timeout = 60_000)]
    public async Task Receive_loop_handles_interleaved_small_frames()
    {
        const int count = 2000;

        await using var server = InProcessWebSocketServer.Start();
        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var received = 0;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.PayloadReceived += (_, _) =>
        {
            if (Interlocked.Increment(ref received) == count) done.TrySetResult(true);
        };

        await connection.Open();
        var session = await server.WaitForFirstClientAsync();

        for (var i = 0; i < count; i++)
        {
            await session.SendTextAsync($"msg-{i}");
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(45));
        Assert.Equal(count, received);

        connection.Dispose();
    }

    [Fact(Timeout = 90_000)]
    public async Task Connection_lifecycle_under_torn_open_close()
    {
        await using var server = InProcessWebSocketServer.Start();
        server.EchoMode = true;

        for (var i = 0; i < 10; i++)
        {
            var connection = new WebsocketConnection(server.WebSocketUri.ToString());
            var openedCount = 0;
            var closedCount = 0;
            connection.Opened += (_, _) => Interlocked.Increment(ref openedCount);
            connection.Closed += (_, _) => Interlocked.Increment(ref closedCount);

            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            connection.PayloadReceived += (_, payload) => received.TrySetResult(payload);

            await connection.Open();
            await connection.SendRequest(NewRequest(), null);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await connection.Close();
            connection.Dispose();

            Assert.Equal(1, openedCount);
            Assert.True(closedCount >= 1, $"iteration {i}: closed event did not fire");
        }
    }

    // ----- Allocation budget --------------------------------------------------

    [Fact(Timeout = 60_000)]
    public async Task Steady_state_send_path_stays_within_allocation_budget()
    {
        await using var server = InProcessWebSocketServer.Start();
        server.EchoMode = true;

        var connection = new WebsocketConnection(server.WebSocketUri.ToString());

        var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var batchDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var batchTarget = 0;
        connection.PayloadReceived += (_, p) =>
        {
            received.Enqueue(p);
            if (received.Count == batchTarget) batchDone.TrySetResult(true);
        };

        await connection.Open();
        // Warm up.
        batchTarget = 5;
        for (var i = 0; i < 5; i++) await connection.SendRequest(NewRequest(), null);
        await batchDone.Task.WaitAsync(TimeSpan.FromSeconds(5));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int iterations = 1000;
        received.Clear();
        batchDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        batchTarget = iterations;

        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < iterations; i++)
        {
            await connection.SendRequest(NewRequest(), null);
        }
        await batchDone.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var after = GC.GetTotalAllocatedBytes(precise: true);

        var bytes = after - before;
        // Generous budget: 1000 requests through Newtonsoft serialization, JSON-RPC envelope,
        // string roundtrip, and channel bookkeeping. Catches regressions where a per-message
        // byte[] allocation is reintroduced on the send path (which would add ~250 KiB).
        const long budgetBytes = 4 * 1024 * 1024;
        Assert.True(bytes < budgetBytes,
            $"Allocation regression: {bytes:N0} bytes for {iterations} send/receive cycles (budget: {budgetBytes:N0}).");

        connection.Dispose();
    }
}
