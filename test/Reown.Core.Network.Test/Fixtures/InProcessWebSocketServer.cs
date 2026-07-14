using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Reown.Core.Network.Test.Fixtures;

/// <summary>
///     In-process WebSocket server backed by <see cref="HttpListener"/>. Used to exercise
///     deterministic server behaviour - fragmentation, binary frames, server-initiated close,
///     abrupt TCP RST - without depending on a live relay.
/// </summary>
public sealed class InProcessWebSocketServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly ConcurrentBag<ServerSession> _sessions = new();
    private readonly ConcurrentBag<Task> _sessionTasks = new();
    private readonly TaskCompletionSource<ServerSession> _firstSessionTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Uri WebSocketUri { get; }

    /// <summary>Fired once a client completes the WebSocket upgrade.</summary>
    public event Action<ServerSession>? ClientConnected;

    /// <summary>Fired for every inbound text message on any session.</summary>
    public event Action<ServerSession, string>? TextMessageReceived;

    /// <summary>
    ///     Optional override - controls how new sessions handle incoming messages by default.
    ///     Setting <c>EchoMode = true</c> echoes every text frame back to the sender.
    /// </summary>
    public bool EchoMode { get; set; }

    /// <summary>
    ///     If true, the server consumes the TCP connection but never responds to the HTTP upgrade
    ///     (used to test client-side connect timeout). Setting this requires using
    ///     <see cref="CreateUnresponsive"/>; this constructor produces a normal server.
    /// </summary>
    private InProcessWebSocketServer(int port)
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        WebSocketUri = new Uri($"ws://127.0.0.1:{port}/");
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public static InProcessWebSocketServer Start()
    {
        var port = ReserveLoopbackPort();
        // small retry loop for the TOCTOU window between probe-close and listener-start
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return new InProcessWebSocketServer(port);
            }
            catch (HttpListenerException) when (attempt < 4)
            {
                port = ReserveLoopbackPort();
            }
            catch (SocketException) when (attempt < 4)
            {
                port = ReserveLoopbackPort();
            }
        }
        throw new InvalidOperationException("Failed to start InProcessWebSocketServer after 5 attempts");
    }

    private static int ReserveLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    public Task<ServerSession> WaitForFirstClientAsync(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(5);
        return _firstSessionTcs.Task.WaitAsync(t);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }

            _sessionTasks.Add(Task.Run(async () =>
            {
                HttpListenerWebSocketContext wsContext;
                try
                {
                    wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                }
                catch
                {
                    return;
                }

                var session = new ServerSession(wsContext.WebSocket, this);
                _sessions.Add(session);
                _firstSessionTcs.TrySetResult(session);
                ClientConnected?.Invoke(session);

                await session.RunAsync(_cts.Token).ConfigureAwait(false);
            }));
        }
    }

    internal void RaiseTextReceived(ServerSession session, string text)
    {
        TextMessageReceived?.Invoke(session, text);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        foreach (var session in _sessions)
        {
            try { await session.AbortAsync().ConfigureAwait(false); } catch { }
        }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { await _acceptLoop.ConfigureAwait(false); } catch { }
        try { await Task.WhenAll(_sessionTasks).ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }

    public sealed class ServerSession : IAsyncDisposable
    {
        private readonly WebSocket _socket;
        private readonly InProcessWebSocketServer _owner;
        private readonly TaskCompletionSource<object?> _completed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> ReceivedTextMessages { get; } = new();

        internal ServerSession(WebSocket socket, InProcessWebSocketServer owner)
        {
            _socket = socket;
            _owner = owner;
        }

        public WebSocketState State => _socket.State;
        public Task Completed => _completed.Task;

        internal async Task RunAsync(CancellationToken ct)
        {
            var buffer = new byte[8192];
            try
            {
                while (_socket.State == WebSocketState.Open)
                {
                    var ms = new System.IO.MemoryStream();
                    WebSocketReceiveResult result;
                    WebSocketMessageType firstType;
                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            try
                            {
                                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "ack", CancellationToken.None).ConfigureAwait(false);
                            }
                            catch { }
                            return;
                        }
                        ms.Write(buffer, 0, result.Count);
                        firstType = result.MessageType;
                    } while (!result.EndOfMessage);

                    if (firstType == WebSocketMessageType.Text)
                    {
                        var text = Encoding.UTF8.GetString(ms.ToArray());
                        lock (ReceivedTextMessages) { ReceivedTextMessages.Add(text); }
                        _owner.RaiseTextReceived(this, text);
                        if (_owner.EchoMode)
                        {
                            await SendTextAsync(text, ct).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (Exception) { }
            finally
            {
                _completed.TrySetResult(null);
            }
        }

        public async Task SendTextAsync(string text, CancellationToken ct = default)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        public async Task SendFragmentedTextAsync(IEnumerable<byte[]> fragments, CancellationToken ct = default)
        {
            var list = new List<byte[]>(fragments);
            for (var i = 0; i < list.Count; i++)
            {
                var endOfMessage = i == list.Count - 1;
                await _socket.SendAsync(new ArraySegment<byte>(list[i]), WebSocketMessageType.Text, endOfMessage, ct).ConfigureAwait(false);
            }
        }

        public async Task SendBinaryAsync(byte[] data, CancellationToken ct = default)
        {
            await _socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }

        public async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken ct = default)
        {
            try
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(status, description, ct).ConfigureAwait(false);
                }
            }
            catch { }
        }

        public Task AbortAsync()
        {
            try { _socket.Abort(); } catch { }
            try { _socket.Dispose(); } catch { }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            try { _socket.Abort(); } catch { }
            try { _socket.Dispose(); } catch { }
            return default;
        }
    }
}

/// <summary>
///     Bare-metal WebSocket server backed by a raw <see cref="TcpListener"/>. Completes the RFC 6455
///     opening handshake by hand and exposes the underlying <see cref="TcpClient"/> so tests can
///     force a TCP RST (via <c>LingerOption(true, 0)</c>) - something <see cref="HttpListener"/>
///     does not surface, and which behaves inconsistently across .NET versions.
/// </summary>
public sealed class RawWebSocketServer : IDisposable
{
    private static readonly byte[] WsHandshakeMagic = System.Text.Encoding.ASCII.GetBytes("258EAFA5-E914-47DA-95CA-C5AB0DC85B11");

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly System.Collections.Concurrent.ConcurrentBag<TcpClient> _clients = new();
    private readonly TaskCompletionSource<TcpClient> _firstClient = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Uri WebSocketUri { get; }

    public RawWebSocketServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        WebSocketUri = new Uri($"ws://127.0.0.1:{port}/");
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            _clients.Add(client);
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandshakeAsync(client).ConfigureAwait(false);
                }
                catch
                {
                    // handshake failed; let the connection die naturally
                }
                _firstClient.TrySetResult(client);
            });
        }
    }

    private static async Task HandshakeAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var ms = new System.IO.MemoryStream();
        var buf = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
            if (read == 0) return;
            ms.Write(buf, 0, read);
            var len = (int)ms.Length;
            var bytes = ms.GetBuffer();
            if (len >= 4 && bytes[len - 4] == (byte)'\r' && bytes[len - 3] == (byte)'\n'
                         && bytes[len - 2] == (byte)'\r' && bytes[len - 1] == (byte)'\n')
            {
                break;
            }
            if (len > 64 * 1024) return; // safety cap
        }

        var header = System.Text.Encoding.ASCII.GetString(ms.ToArray());
        string? key = null;
        foreach (var line in header.Split('\n'))
        {
            const string prefix = "Sec-WebSocket-Key:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                key = line.Substring(prefix.Length).Trim();
            }
        }
        if (key == null) return;

        var inputBytes = System.Text.Encoding.ASCII.GetBytes(key);
        var combined = new byte[inputBytes.Length + WsHandshakeMagic.Length];
        Buffer.BlockCopy(inputBytes, 0, combined, 0, inputBytes.Length);
        Buffer.BlockCopy(WsHandshakeMagic, 0, combined, inputBytes.Length, WsHandshakeMagic.Length);
        var hash = System.Security.Cryptography.SHA1.HashData(combined);
        var accept = Convert.ToBase64String(hash);

        var resp = "HTTP/1.1 101 Switching Protocols\r\n" +
                   "Upgrade: websocket\r\n" +
                   "Connection: Upgrade\r\n" +
                   "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
        var respBytes = System.Text.Encoding.ASCII.GetBytes(resp);
        await stream.WriteAsync(respBytes, 0, respBytes.Length).ConfigureAwait(false);
    }

    public Task<TcpClient> WaitForFirstClientAsync(TimeSpan? timeout = null)
    {
        var t = timeout ?? TimeSpan.FromSeconds(5);
        return _firstClient.Task.WaitAsync(t);
    }

    /// <summary>Force a real TCP RST on the given client connection.</summary>
    public void AbortClient(TcpClient client)
    {
        try { client.Client.LingerState = new LingerOption(true, 0); } catch { }
        try { client.Client.Close(0); } catch { }
        try { client.Close(); } catch { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        foreach (var c in _clients)
        {
            try { c.Close(); } catch { }
        }
        try { _acceptLoop.Wait(500); } catch { }
        _cts.Dispose();
    }
}

/// <summary>
///     Raw TCP listener that accepts connections but never completes the HTTP upgrade.
///     Used to exercise the client's connect timeout.
/// </summary>
public sealed class UnresponsiveTcpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly List<TcpClient> _connections = new();

    public Uri WebSocketUri { get; }

    public UnresponsiveTcpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        WebSocketUri = new Uri($"ws://127.0.0.1:{port}/");
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                lock (_connections) _connections.Add(client);
            }
            catch
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        lock (_connections)
        {
            foreach (var c in _connections)
            {
                try { c.Close(); } catch { }
            }
        }
        _cts.Dispose();
    }
}
