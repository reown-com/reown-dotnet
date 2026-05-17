using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Reown.Core.Common.Logging;

namespace Reown.Core.Network.Websocket.Internal
{
    /// <summary>
    ///     Hand-rolled <see cref="ClientWebSocket" /> wrapper exposing the same observable behaviour
    ///     the <see cref="WebsocketConnection" /> class needs: <c>Opened</c>, <c>PayloadReceived</c>,
    ///     <c>ErrorReceived</c>, and <c>Closed</c> events plus a thread-safe <see cref="SendAsync" />
    ///     entry point and graceful <see cref="StopAsync" /> shutdown.
    /// </summary>
    internal sealed class ClientWebSocketTransport : IDisposable
    {
        private const int ReceiveBufferSize = 4096;
        private const int SendQueueCapacity = 256;
        private const int SendDrainTimeoutMs = 2000;
        private const int CloseOutputTimeoutMs = 1500;
        private const int ServerCloseReplyTimeoutMs = 3000;

        private readonly CancellationTokenSource _cts = new();
        private readonly object _gate = new();
        private readonly TimeSpan _keepAlive;
        private readonly ILogger _logger;
        private readonly TimeSpan _openTimeout;

        private readonly Channel<PooledSendBuffer> _outbox = Channel.CreateBounded<PooledSendBuffer>(
            new BoundedChannelOptions(SendQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        private readonly Uri _uri;

        private ClientWebSocket _client;
        private int _closedRaised;
        private bool _disposed;
        private volatile bool _faulted;
        private PooledByteBufferWriter _reassembly;
        private Task _receiveLoop;
        private Task _sendLoop;
        private volatile bool _serverInitiatedClose;
        private volatile bool _shutdownRequested;

        public ClientWebSocketTransport(Uri uri, TimeSpan openTimeout, TimeSpan keepAlive, ILogger logger = null)
        {
            _uri = uri ?? throw new ArgumentNullException(nameof(uri));
            _openTimeout = openTimeout;
            _keepAlive = keepAlive;
            _logger = logger;
        }

        /// <summary>
        ///     Current state of the underlying <see cref="ClientWebSocket" />, or <see cref="WebSocketState.None" /> before <see cref="StartAsync" />.
        /// </summary>
        public WebSocketState State
        {
            get
            {
                var client = _client;
                return client?.State ?? WebSocketState.None;
            }
        }

        /// <summary>
        ///     The keep-alive interval reported by the underlying client; falls back to the configured value before <see cref="StartAsync" />.
        /// </summary>
        internal TimeSpan KeepAliveInterval
        {
            get
            {
                var client = _client;
                return client != null ? client.Options.KeepAliveInterval : _keepAlive;
            }
        }

        /// <summary>
        ///     True once the transport has exited due to a non-shutdown error (server abort,
        ///     unexpected socket close). Further calls to <see cref="SendAsync" /> will throw
        ///     <see cref="ChannelClosedException" /> with the original failure as InnerException.
        /// </summary>
        public bool Faulted
        {
            get => _faulted;
        }

        /// <summary>
        ///     True once <see cref="StopAsync" /> or <see cref="Dispose" /> has been invoked.
        ///     Used by <c>WebsocketConnection</c> to distinguish client-initiated close from
        ///     server-initiated close so it doesn't double-clear its transport reference.
        /// </summary>
        public bool ShutdownRequested
        {
            get => _shutdownRequested;
        }

        /// <summary>
        ///     Aborts in-flight operations, disposes the underlying socket, and releases
        ///     pooled resources. Idempotent.
        /// </summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            _shutdownRequested = true;

            try
            {
                _outbox.Writer.TryComplete();
            }
            catch
            {
            }

            try
            {
                _cts.Cancel();
            }
            catch
            {
                // ignored
            }

            // Disposing the underlying client forces in-flight ReceiveAsync/SendAsync calls to
            // fault, so capture the loop tasks first then wait for them after the socket dies.
            var receiveLoop = _receiveLoop;
            var sendLoop = _sendLoop;

            try
            {
                _client?.Dispose();
            }
            catch
            {
                // ignored
            }

            // Now that the socket is dead, the loops will exit promptly. Wait so neither of
            // them touches _cts after we dispose it below.
            try
            {
                receiveLoop?.Wait(1000);
            }
            catch
            {
                // ignored
            }

            try
            {
                sendLoop?.Wait(1000);
            }
            catch
            {
                // ignored
            }

            try
            {
                _cts.Dispose();
            }
            catch
            {
                // ignored
            }

            _reassembly?.Dispose();
            _reassembly = null;

            DrainOutbox();
            RaiseClosed();
        }

        public event EventHandler<string> PayloadReceived;
        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<Exception> ErrorReceived;

        /// <summary>
        ///     Connects to the configured URI and starts the receive/send loops. Raises
        ///     <see cref="Opened" /> on success; throws <see cref="TimeoutException" /> if
        ///     the connect exceeds the configured open timeout.
        /// </summary>
        public async Task StartAsync(CancellationToken externalToken)
        {
            ThrowIfDisposed();

            var client = new ClientWebSocket();
            client.Options.KeepAliveInterval = _keepAlive;
#if !NETSTANDARD2_1
            client.Options.HttpVersion = System.Net.HttpVersion.Version11;
#endif

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken, _cts.Token))
            {
                connectCts.CancelAfter(_openTimeout);
                try
                {
                    await client.ConnectAsync(_uri, connectCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested || _disposed)
                {
                    AbortAndDispose(client);
                    throw new ObjectDisposedException(nameof(ClientWebSocketTransport));
                }
                catch (OperationCanceledException) when (!externalToken.IsCancellationRequested
                                                         && connectCts.IsCancellationRequested)
                {
                    AbortAndDispose(client);
                    throw new TimeoutException("WebSocket connect to " + _uri + " exceeded " + _openTimeout + ".");
                }
                catch
                {
                    AbortAndDispose(client);
                    throw;
                }
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    AbortAndDispose(client);
                    throw new ObjectDisposedException(nameof(ClientWebSocketTransport));
                }

                _client = client;

                var loopToken = _cts.Token;
                _receiveLoop = ReceiveLoopAsync(loopToken);
                _sendLoop = SendLoopAsync(loopToken);
            }

            Opened?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        ///     Serializes <paramref name="json" /> as UTF-8 and enqueues it for the send loop.
        ///     Buffers are pooled; ownership transfers to the send loop once accepted by the channel.
        /// </summary>
        public async ValueTask SendAsync(string json, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (json == null) throw new ArgumentNullException(nameof(json));

            while (await _outbox.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
            {
                var maxBytes = Encoding.UTF8.GetMaxByteCount(json.Length);
                var buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
                var transferred = false;
                try
                {
                    var written = Encoding.UTF8.GetBytes(json, 0, json.Length, buffer, 0);
                    if (_outbox.Writer.TryWrite(new PooledSendBuffer(buffer, written)))
                    {
                        transferred = true;
                        return;
                    }
                }
                finally
                {
                    if (!transferred) ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            throw new ChannelClosedException();
        }

        /// <summary>
        ///     Gracefully shuts down the transport: drains queued sends, emits a Close frame with
        ///     <paramref name="closeStatus" />/<paramref name="statusDescription" />, then waits for
        ///     the server's close reply. Always raises <see cref="Closed" /> on completion.
        /// </summary>
        public async Task StopAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken = default)
        {
            if (_shutdownRequested)
            {
                RaiseClosed();
                return;
            }

            _shutdownRequested = true;

            // Stop accepting new sends so the consumer can drain naturally.
            _outbox.Writer.TryComplete();

            // Wait for the send loop to finish flushing queued items (bounded).
            var sendLoop = _sendLoop;
            var sendLoopDrained = sendLoop == null;
            if (sendLoop != null)
            {
                var completed = await Task.WhenAny(sendLoop, Task.Delay(SendDrainTimeoutMs, cancellationToken)).ConfigureAwait(false);
                sendLoopDrained = ReferenceEquals(completed, sendLoop);
            }

            // Send the close frame on a still-clean send side.
            try
            {
                var client = _client;
                if (sendLoopDrained && client is { State: WebSocketState.Open or WebSocketState.CloseReceived })
                {
                    using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    closeCts.CancelAfter(CloseOutputTimeoutMs);
                    try
                    {
                        await client.CloseOutputAsync(closeStatus, statusDescription, closeCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // timeout or caller-cancelled - proceed
                    }
                }
                else if (!sendLoopDrained)
                {
                    try
                    {
                        _cts.Cancel();
                        client?.Abort();
                    }
                    catch
                    {
                        // best effort
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Log($"CloseOutputAsync error: {ex.Message}");
            }

            // Give the receive loop a chance to observe the server's close reply.
            var receiveLoop = _receiveLoop;
            if (receiveLoop != null)
            {
                await Task.WhenAny(receiveLoop, Task.Delay(ServerCloseReplyTimeoutMs, cancellationToken)).ConfigureAwait(false);
            }

            // Force any remaining awaits to unblock.
            try
            {
                _cts.Cancel();
            }
            catch
            {
                // already cancelled
            }

            try
            {
                if (receiveLoop != null && sendLoop != null)
                {
                    await Task.WhenAll(receiveLoop, sendLoop).ConfigureAwait(false);
                }
            }
            catch
            {
                // Expected - both loops fault on cancellation.
            }

            RaiseClosed();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var recvBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            try
            {
                while (true)
                {
                    int count;
                    bool endOfMessage;
                    WebSocketMessageType messageType;

#if NETSTANDARD2_1
                    var result = await _client.ReceiveAsync(
                        new ArraySegment<byte>(recvBuffer, 0, recvBuffer.Length),
                        cancellationToken).ConfigureAwait(false);
                    count = result.Count;
                    endOfMessage = result.EndOfMessage;
                    messageType = result.MessageType;
#else
                    var result = await _client.ReceiveAsync(
                        recvBuffer.AsMemory(),
                        cancellationToken).ConfigureAwait(false);
                    count = result.Count;
                    endOfMessage = result.EndOfMessage;
                    messageType = result.MessageType;
#endif

                    switch (messageType)
                    {
                        case WebSocketMessageType.Close:
                            await HandleServerInitiatedCloseAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        case WebSocketMessageType.Binary:
                        {
                            // Reown only handles text frames - discard binary entirely, including continuation frames.
                            while (!endOfMessage)
                            {
#if NETSTANDARD2_1
                                var more = await _client.ReceiveAsync(
                                    new ArraySegment<byte>(recvBuffer, 0, recvBuffer.Length),
                                    cancellationToken).ConfigureAwait(false);
                                endOfMessage = more.EndOfMessage;
                                if (more.MessageType != WebSocketMessageType.Close)
                                    continue;
                                await HandleServerInitiatedCloseAsync(cancellationToken).ConfigureAwait(false);
                                return;
#else
                                var more = await _client.ReceiveAsync(
                                    recvBuffer.AsMemory(),
                                    cancellationToken).ConfigureAwait(false);
                                endOfMessage = more.EndOfMessage;
                                if (more.MessageType != WebSocketMessageType.Close)
                                    continue;
                                await HandleServerInitiatedCloseAsync(cancellationToken).ConfigureAwait(false);
                                return;
#endif
                            }

                            continue;
                        }
                    }

                    if (endOfMessage && _reassembly == null)
                    {
                        RaiseIfNotEmpty(Encoding.UTF8.GetString(recvBuffer, 0, count));
                    }
                    else
                    {
                        if (_reassembly == null)
                            _reassembly = new PooledByteBufferWriter(ReceiveBufferSize);

                        _reassembly.Write(recvBuffer, 0, count);

                        if (endOfMessage)
                        {
                            var payload = _reassembly.GetString();
                            _reassembly.Dispose();
                            _reassembly = null;
                            RaiseIfNotEmpty(payload);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownRequested || cancellationToken.IsCancellationRequested)
            {
                // Intentional shutdown - swallow.
            }
            catch (Exception ex)
            {
                if (!_shutdownRequested)
                {
                    _faulted = true;
                    _outbox.Writer.TryComplete(ex);
                    try
                    {
                        ErrorReceived?.Invoke(this, ex);
                    }
                    catch
                    {
                        // user code
                    }
                }
                else
                {
                    _logger?.Log("Receive loop terminated during shutdown: " + ex.Message);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(recvBuffer);
                _reassembly?.Dispose();
                _reassembly = null;
                RaiseClosed();
            }
        }

        private async Task HandleServerInitiatedCloseAsync(CancellationToken cancellationToken)
        {
            // RFC 6455 5.5.1: respond with a Close frame. Defer the actual send to the send
            // loop so we don't race the single-outstanding-send invariant of ClientWebSocket.
            // We must not let the receive loop's finally raise Closed until the close
            // handshake has completed - otherwise consumers see the close before our own
            // close frame reaches the wire.
            _serverInitiatedClose = true;
            _outbox.Writer.TryComplete();

            var sendLoop = _sendLoop;
            if (sendLoop != null)
            {
                try
                {
                    await Task.WhenAny(sendLoop, Task.Delay(CloseOutputTimeoutMs * 2, cancellationToken))
                        .ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
            }
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await _outbox.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (_outbox.Reader.TryRead(out var item))
                    {
                        try
                        {
#if NETSTANDARD2_1
                            await _client.SendAsync(
                                new ArraySegment<byte>(item.Buffer, 0, item.Length),
                                WebSocketMessageType.Text,
                                endOfMessage: true,
                                cancellationToken).ConfigureAwait(false);
#else
                            await _client.SendAsync(
                                item.Buffer.AsMemory(0, item.Length),
                                WebSocketMessageType.Text,
                                true,
                                cancellationToken).ConfigureAwait(false);
#endif
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(item.Buffer);
                        }
                    }
                }

                // Writer completed. If the server initiated a close handshake, reply with our
                // own Close frame here - this is the only safe place because we hold the
                // exclusive send side.
                if (_serverInitiatedClose && !_shutdownRequested)
                {
                    try
                    {
                        var client = _client;
                        if (client != null && client.State == WebSocketState.CloseReceived)
                        {
                            using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            closeCts.CancelAfter(CloseOutputTimeoutMs);
                            await client.CloseOutputAsync(
                                client.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                                client.CloseStatusDescription ?? string.Empty,
                                closeCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Log("Close-back failed: " + ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownRequested || cancellationToken.IsCancellationRequested)
            {
                // Intentional shutdown.
            }
            catch (ChannelClosedException)
            {
                // Writer completed - normal termination path.
            }
            catch (Exception ex)
            {
                if (!_shutdownRequested)
                {
                    _faulted = true;
                    _outbox.Writer.TryComplete(ex);
                    try
                    {
                        ErrorReceived?.Invoke(this, ex);
                    }
                    catch
                    {
                        // user code
                    }
                }
                else
                {
                    _logger?.Log("Send loop terminated during shutdown: " + ex.Message);
                }
            }
            finally
            {
                DrainOutbox();
            }
        }

        private void RaiseIfNotEmpty(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;
            try
            {
                PayloadReceived?.Invoke(this, payload);
            }
            catch
            {
                // user code
            }
        }

        private void RaiseClosed()
        {
            if (Interlocked.Exchange(ref _closedRaised, 1) != 0)
                return;
            try
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // user code
            }
        }

        private void DrainOutbox()
        {
            while (_outbox.Reader.TryRead(out var item))
            {
                ArrayPool<byte>.Shared.Return(item.Buffer);
            }
        }

        private static void AbortAndDispose(ClientWebSocket client)
        {
            try
            {
                client.Abort();
            }
            catch
            {
                // best effort
            }

            client.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ClientWebSocketTransport));
        }

        /// <summary>
        ///     Pairs a pooled byte buffer with the number of valid bytes it holds. Ownership of the
        ///     buffer transfers to the channel consumer once an instance is enqueued.
        /// </summary>
        private readonly struct PooledSendBuffer
        {
            /// <summary>The pooled byte array containing the serialized payload.</summary>
            public readonly byte[] Buffer;

            /// <summary>The number of valid bytes in <see cref="Buffer" />.</summary>
            public readonly int Length;

            /// <summary>Wraps a rented <paramref name="buffer" /> with its valid <paramref name="length" />.</summary>
            public PooledSendBuffer(byte[] buffer, int length)
            {
                Buffer = buffer;
                Length = length;
            }
        }

        /// <summary>
        ///     Minimal append-only byte writer backed by <see cref="ArrayPool{T}.Shared" />. Used to
        ///     accumulate fragmented WebSocket frames before a single UTF-8 decode at <c>EndOfMessage</c>.
        /// </summary>
        internal sealed class PooledByteBufferWriter : IDisposable
        {
            private const int MaxReassemblyBytes = 16 * 1024 * 1024;

            private byte[] _buffer;

            /// <summary>
            ///     Rents a pooled buffer of at least <paramref name="initialCapacity" /> bytes.
            /// </summary>
            public PooledByteBufferWriter(int initialCapacity)
            {
                _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
                Length = 0;
            }

            /// <summary>
            ///     Total bytes written so far.
            /// </summary>
            public int Length { get; private set; }

            /// <summary>
            ///     Returns the pooled buffer atomically; safe to call multiple times.
            /// </summary>
            public void Dispose()
            {
                var buf = Interlocked.Exchange(ref _buffer, null);
                if (buf != null) ArrayPool<byte>.Shared.Return(buf);
            }

            /// <summary>
            ///     Appends <paramref name="count" /> bytes from <paramref name="source" /> starting at <paramref name="offset" />.
            /// </summary>
            public void Write(byte[] source, int offset, int count)
            {
                if (count == 0) return;
                EnsureCapacity(Length + count);
                Buffer.BlockCopy(source, offset, _buffer, Length, count);
                Length += count;
            }

            /// <summary>
            ///     Decodes the accumulated bytes as UTF-8.
            /// </summary>
            public string GetString()
            {
                return Encoding.UTF8.GetString(_buffer, 0, Length);
            }

            private void EnsureCapacity(int needed)
            {
                if (_buffer.Length >= needed) return;
                if (needed > MaxReassemblyBytes)
                    throw new InvalidDataException("WebSocket message exceeds " + MaxReassemblyBytes + " bytes.");
                var newSize = Math.Max(_buffer.Length * 2, needed);
                var newBuf = ArrayPool<byte>.Shared.Rent(newSize);
                Buffer.BlockCopy(_buffer, 0, newBuf, 0, Length);
                var old = _buffer;
                _buffer = newBuf;
                ArrayPool<byte>.Shared.Return(old);
            }
        }
    }
}
