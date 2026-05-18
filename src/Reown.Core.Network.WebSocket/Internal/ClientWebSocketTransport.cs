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

            var maxBytes = Encoding.UTF8.GetMaxByteCount(json.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(maxBytes);
            var transferred = false;
            try
            {
                var written = Encoding.UTF8.GetBytes(json, 0, json.Length, buffer, 0);
                await _outbox.Writer.WriteAsync(new PooledSendBuffer(buffer, written), cancellationToken)
                    .ConfigureAwait(false);
                transferred = true;
            }
            finally
            {
                if (!transferred) ArrayPool<byte>.Shared.Return(buffer);
            }
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

            var sendLoop = _sendLoop;
            var sendLoopDrained = await WaitForSendLoopDrainAsync(sendLoop, cancellationToken).ConfigureAwait(false);

            await CloseOutputOrAbortAsync(sendLoopDrained, closeStatus, statusDescription, cancellationToken)
                .ConfigureAwait(false);

            var receiveLoop = _receiveLoop;
            await WaitForServerCloseReplyAsync(receiveLoop, cancellationToken).ConfigureAwait(false);

            CancelTransportOperations();
            await AwaitLoopsQuietlyAsync(receiveLoop, sendLoop).ConfigureAwait(false);

            RaiseClosed();
        }

        /// <summary>
        ///     Waits briefly for the send loop to flush queued messages before close output is attempted.
        /// </summary>
        private static async Task<bool> WaitForSendLoopDrainAsync(Task sendLoop, CancellationToken cancellationToken)
        {
            if (sendLoop == null)
                return true;

            var completed = await Task.WhenAny(sendLoop, Task.Delay(SendDrainTimeoutMs, cancellationToken))
                .ConfigureAwait(false);
            return ReferenceEquals(completed, sendLoop);
        }

        /// <summary>
        ///     Emits a close frame when the send side drained, otherwise aborts to unblock pending operations.
        /// </summary>
        private async Task CloseOutputOrAbortAsync(
            bool sendLoopDrained,
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken)
        {
            try
            {
                var client = _client;
                if (!sendLoopDrained)
                {
                    CancelTransportOperations();
                    AbortClient(client);
                    return;
                }

                if (client is { State: WebSocketState.Open or WebSocketState.CloseReceived })
                    await TryCloseOutputAsync(client, closeStatus, statusDescription, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Log($"CloseOutputAsync error: {ex.Message}");
            }
        }

        /// <summary>
        ///     Sends the WebSocket close output frame using a short timeout.
        /// </summary>
        private static async Task TryCloseOutputAsync(
            ClientWebSocket client,
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken)
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

        /// <summary>
        ///     Gives the receive loop a bounded window to observe the server's close reply.
        /// </summary>
        private static async Task WaitForServerCloseReplyAsync(Task receiveLoop, CancellationToken cancellationToken)
        {
            if (receiveLoop == null)
                return;

            await Task.WhenAny(receiveLoop, Task.Delay(ServerCloseReplyTimeoutMs, cancellationToken))
                .ConfigureAwait(false);
        }

        /// <summary>
        ///     Cancels the shared transport token source while tolerating concurrent disposal.
        /// </summary>
        private void CancelTransportOperations()
        {
            try
            {
                _cts.Cancel();
            }
            catch
            {
                // already cancelled
            }
        }

        /// <summary>
        ///     Waits for both loops after cancellation while swallowing expected shutdown faults.
        /// </summary>
        private static async Task AwaitLoopsQuietlyAsync(Task receiveLoop, Task sendLoop)
        {
            if (receiveLoop == null || sendLoop == null)
                return;

            try
            {
                await Task.WhenAll(receiveLoop, sendLoop).ConfigureAwait(false);
            }
            catch
            {
                // Expected - both loops fault on cancellation.
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var recvBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
            try
            {
                await ReceiveMessagesAsync(recvBuffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdownRequested || cancellationToken.IsCancellationRequested)
            {
                // Intentional shutdown - swallow.
            }
            catch (Exception ex)
            {
                HandleLoopError(ex, "Receive");
            }
            finally
            {
                ReturnReceiveBuffer(recvBuffer);
                RaiseClosed();
            }
        }

        /// <summary>
        ///     Receives frames until the WebSocket is closed or cancellation stops the loop.
        /// </summary>
        private async Task ReceiveMessagesAsync(byte[] recvBuffer, CancellationToken cancellationToken)
        {
            while (true)
            {
                var frame = await ReceiveFrameAsync(recvBuffer, cancellationToken).ConfigureAwait(false);
                if (await ReceiveFrameOrCloseAsync(recvBuffer, frame, cancellationToken).ConfigureAwait(false))
                    return;
            }
        }

        /// <summary>
        ///     Handles a received frame and returns true when the receive loop should stop.
        /// </summary>
        private async Task<bool> ReceiveFrameOrCloseAsync(
            byte[] recvBuffer,
            ReceiveFrame frame,
            CancellationToken cancellationToken)
        {
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                await HandleServerInitiatedCloseAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (frame.MessageType == WebSocketMessageType.Binary)
                return await DiscardBinaryMessageAsync(recvBuffer, frame.EndOfMessage, cancellationToken)
                    .ConfigureAwait(false);

            HandleTextFrame(recvBuffer, frame);
            return false;
        }

        /// <summary>
        ///     Reads a WebSocket frame into the supplied pooled buffer.
        /// </summary>
        private async Task<ReceiveFrame> ReceiveFrameAsync(byte[] recvBuffer, CancellationToken cancellationToken)
        {
#if NETSTANDARD2_1
            var result = await _client.ReceiveAsync(
                new ArraySegment<byte>(recvBuffer, 0, recvBuffer.Length),
                cancellationToken).ConfigureAwait(false);
#else
            var result = await _client.ReceiveAsync(
                recvBuffer.AsMemory(),
                cancellationToken).ConfigureAwait(false);
#endif
            return new ReceiveFrame(result.Count, result.EndOfMessage, result.MessageType);
        }

        /// <summary>
        ///     Discards a binary message, returning true if a close frame interrupts the discard.
        /// </summary>
        private async Task<bool> DiscardBinaryMessageAsync(
            byte[] recvBuffer,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            while (!endOfMessage)
            {
                var frame = await ReceiveFrameAsync(recvBuffer, cancellationToken).ConfigureAwait(false);
                if (frame.MessageType == WebSocketMessageType.Close)
                {
                    await HandleServerInitiatedCloseAsync(cancellationToken).ConfigureAwait(false);
                    return true;
                }

                endOfMessage = frame.EndOfMessage;
            }

            return false;
        }

        /// <summary>
        ///     Dispatches a complete text frame or appends a text fragment for later reassembly.
        /// </summary>
        private void HandleTextFrame(byte[] recvBuffer, ReceiveFrame frame)
        {
            if (frame.EndOfMessage && _reassembly == null)
            {
                RaiseIfNotEmpty(Encoding.UTF8.GetString(recvBuffer, 0, frame.Count));
                return;
            }

            AppendTextFragment(recvBuffer, frame.Count);

            if (frame.EndOfMessage)
                RaiseReassembledPayload();
        }

        /// <summary>
        ///     Appends bytes from the current frame to the active reassembly buffer.
        /// </summary>
        private void AppendTextFragment(byte[] recvBuffer, int count)
        {
            if (_reassembly == null)
                _reassembly = new PooledByteBufferWriter(ReceiveBufferSize);

            _reassembly.Write(recvBuffer, 0, count);
        }

        /// <summary>
        ///     Emits and releases the active reassembled text payload.
        /// </summary>
        private void RaiseReassembledPayload()
        {
            var payload = _reassembly.GetString();
            _reassembly.Dispose();
            _reassembly = null;
            RaiseIfNotEmpty(payload);
        }

        /// <summary>
        ///     Returns the receive buffer and releases any partial message reassembly.
        /// </summary>
        private void ReturnReceiveBuffer(byte[] recvBuffer)
        {
            ArrayPool<byte>.Shared.Return(recvBuffer);
            _reassembly?.Dispose();
            _reassembly = null;
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
                await SendQueuedItemsAsync(cancellationToken).ConfigureAwait(false);
                await SendCloseReplyIfNeededAsync(cancellationToken).ConfigureAwait(false);
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
                HandleLoopError(ex, "Send");
            }
            finally
            {
                DrainOutbox();
            }
        }

        /// <summary>
        ///     Drains queued payloads and sends them sequentially on the single WebSocket send side.
        /// </summary>
        private async Task SendQueuedItemsAsync(CancellationToken cancellationToken)
        {
            while (await _outbox.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_outbox.Reader.TryRead(out var item))
                {
                    await SendItemAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        ///     Sends a pooled payload and returns its buffer to the shared pool.
        /// </summary>
        private async Task SendItemAsync(PooledSendBuffer item, CancellationToken cancellationToken)
        {
            try
            {
                await SendBufferAsync(item, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(item.Buffer);
            }
        }

        /// <summary>
        ///     Sends a text payload from a pooled buffer.
        /// </summary>
        private async Task SendBufferAsync(PooledSendBuffer item, CancellationToken cancellationToken)
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

        /// <summary>
        ///     Replies to a server-initiated close after all queued sends have drained.
        /// </summary>
        private async Task SendCloseReplyIfNeededAsync(CancellationToken cancellationToken)
        {
            if (!_serverInitiatedClose || _shutdownRequested)
                return;

            try
            {
                await SendCloseReplyAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Log("Close-back failed: " + ex.Message);
            }
        }

        /// <summary>
        ///     Sends the close reply while this loop owns the exclusive send side.
        /// </summary>
        private async Task SendCloseReplyAsync(CancellationToken cancellationToken)
        {
            var client = _client;
            if (client == null || client.State != WebSocketState.CloseReceived)
                return;

            using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            closeCts.CancelAfter(CloseOutputTimeoutMs);
            await client.CloseOutputAsync(
                client.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                client.CloseStatusDescription ?? string.Empty,
                closeCts.Token).ConfigureAwait(false);
        }

        /// <summary>
        ///     Reports loop failures unless they are part of an intentional shutdown.
        /// </summary>
        private void HandleLoopError(Exception ex, string loopName)
        {
            if (!_shutdownRequested)
            {
                _faulted = true;
                _outbox.Writer.TryComplete(ex);
                RaiseErrorReceived(ex);
                return;
            }

            _logger?.Log(loopName + " loop terminated during shutdown: " + ex.Message);
        }

        /// <summary>
        ///     Raises <see cref="ErrorReceived" /> while isolating subscriber exceptions.
        /// </summary>
        private void RaiseErrorReceived(Exception ex)
        {
            try
            {
                ErrorReceived?.Invoke(this, ex);
            }
            catch
            {
                // user code
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

        /// <summary>
        ///     Aborts a WebSocket client while tolerating concurrent shutdown races.
        /// </summary>
        private static void AbortClient(ClientWebSocket client)
        {
            try
            {
                client?.Abort();
            }
            catch
            {
                // best effort
            }
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
        ///     Captures the frame metadata returned by <see cref="ClientWebSocket.ReceiveAsync" />.
        /// </summary>
        private readonly struct ReceiveFrame
        {
            /// <summary>The number of bytes written into the receive buffer.</summary>
            public readonly int Count;

            /// <summary>Whether this frame ends the current message.</summary>
            public readonly bool EndOfMessage;

            /// <summary>The WebSocket message type for the received frame.</summary>
            public readonly WebSocketMessageType MessageType;

            /// <summary>Wraps received frame metadata.</summary>
            public ReceiveFrame(int count, bool endOfMessage, WebSocketMessageType messageType)
            {
                Count = count;
                EndOfMessage = endOfMessage;
                MessageType = messageType;
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
