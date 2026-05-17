using System;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Core.Network.Websocket.Internal;

namespace Reown.Core.Network.Websocket
{
    /// <summary>
    ///     A JSON RPC connection backed by <see cref="System.Net.WebSockets.ClientWebSocket"/>.
    /// </summary>
    public class WebsocketConnection : IJsonRpcConnection, IModule
    {
        private static readonly TimeSpan DefaultKeepAliveInterval = TimeSpan.FromSeconds(30);

        private readonly string _context;
        private readonly ILogger _logger;
        private readonly object _registerGate = new object();
        private ClientWebSocketTransport _transport;
        private ClientWebSocketTransport _pendingTransport;
        private bool _pendingTransportClosedEarly;
        private TaskCompletionSource<ClientWebSocketTransport> _pendingRegister;
        private bool _registered;
        private bool _disposed;

        /// <summary>
        ///     Create a new websocket connection that will connect to the given URL
        /// </summary>
        /// <param name="url">The URL to connect to</param>
        /// <param name="context">The context of the root module</param>
        /// <exception cref="ArgumentException">If the given URL is invalid</exception>
        public WebsocketConnection(string url, string context = null)
        {
            if (!Validation.IsWsUrl(url))
                throw new ArgumentException("Provided URL is not compatible with WebSocket connection: " + url);

            _context = context ?? Guid.NewGuid().ToString();
            _logger = ReownLogger.WithContext(Context);
            Url = url;
        }

        /// <summary>
        ///     The Url to connect to
        /// </summary>
        public string Url { get; private set; }

        public bool IsPaused { get; internal set; }

        /// <summary>
        ///     The Open timeout
        /// </summary>
        public TimeSpan OpenTimeout
        {
            get => TimeSpan.FromSeconds(60);
        }

        public event EventHandler<string> PayloadReceived;
        public event EventHandler Closed;
        public event EventHandler<Exception> ErrorReceived;
        public event EventHandler<object> Opened;
        public event EventHandler<Exception> RegisterErrored;

        /// <summary>
        ///     Whether this websocket connection is connected
        /// </summary>
        public bool Connected
        {
            get
            {
                var transport = _transport;
                return transport != null && transport.State == WebSocketState.Open;
            }
        }

        /// <summary>
        ///     Whether this websocket connection is currently connecting
        /// </summary>
        public bool Connecting { get; private set; }

        /// <summary>
        ///     Open this connection
        /// </summary>
        public async Task Open()
        {
            await Register(Url);
        }

        /// <summary>
        ///     Open this connection using a string url
        /// </summary>
        /// <param name="options">Must be a string url. If any other type, then normal Open() is invoked</param>
        /// <typeparam name="T">The type of the options. Should always be string</typeparam>
        public async Task Open<T>(T options)
        {
            if (typeof(string).IsAssignableFrom(typeof(T)))
            {
                await Register(options as string);
            }

            await Open();
        }

        /// <summary>
        ///     Close this connection
        /// </summary>
        /// <exception cref="IOException">If this connection was already closed</exception>
        public async Task Close()
        {
            var transport = _transport;
            if (transport == null)
                throw new IOException("Connection already closed");

            // Authoritative cleanup path for client-initiated close. OnTransportClosed skips
            // clearing _transport while ShutdownRequested == true, so this finally is solely
            // responsible.
            try
            {
                await transport.StopAsync(WebSocketCloseStatus.NormalClosure, "Close Invoked");
            }
            finally
            {
                DetachAndClearTransport(transport);
            }
        }

        /// <summary>
        ///     Send a Json RPC request through this websocket connection, using the given context
        /// </summary>
        /// <param name="requestPayload">The request payload to encode and send</param>
        /// <param name="context">The context to use when sending</param>
        /// <typeparam name="T">The type of the Json RPC request parameter</typeparam>
        public async Task SendRequest<T>(IJsonRpcRequest<T> requestPayload, object context)
        {
            var transport = _transport ?? await Register(Url);

            try
            {
                _logger.Log("Sending request over websocket");
                await transport.SendAsync(JsonConvert.SerializeObject(requestPayload), CancellationToken.None);
            }
            catch (Exception e)
            {
                _logger.Log("Error sending request: " + e.Message);
                OnError<T>(requestPayload, e);
                if (transport.Faulted) DetachAndClearTransport(transport);
            }
        }

        /// <summary>
        ///     Send a Json RPC response through this websocket connection, using the given context
        /// </summary>
        /// <param name="responsePayload">The response payload to encode and send</param>
        /// <param name="context">The context to use when sending</param>
        /// <typeparam name="T">The type of the Json RPC response result</typeparam>
        public async Task SendResult<T>(IJsonRpcResult<T> responsePayload, object context)
        {
            var transport = _transport ?? await Register(Url);

            try
            {
                await transport.SendAsync(JsonConvert.SerializeObject(responsePayload), CancellationToken.None);
            }
            catch (Exception e)
            {
                OnError<T>(responsePayload, e);
                if (transport.Faulted) DetachAndClearTransport(transport);
            }
        }

        /// <summary>
        ///     Send a JSON RPC error. This function does not return or wait for response. JSON RPC errors do not receive
        ///     any response and therefore do not trigger any events
        /// </summary>
        /// <param name="errorPayload">The error to send</param>
        /// <param name="context">The current context</param>
        /// <returns>A task that is performing the send</returns>
        public async Task SendError(IJsonRpcError errorPayload, object context)
        {
            var transport = _transport ?? await Register(Url);

            try
            {
                await transport.SendAsync(JsonConvert.SerializeObject(errorPayload), CancellationToken.None);
            }
            catch (Exception e)
            {
                OnError<object>(errorPayload, e);
                if (transport.Faulted) DetachAndClearTransport(transport);
            }
        }

        private void DetachAndClearTransport(ClientWebSocketTransport transport)
        {
            transport.PayloadReceived -= OnTransportPayload;
            transport.ErrorReceived -= OnTransportError;
            transport.Closed -= OnTransportClosed;

            lock (_registerGate)
            {
                if (ReferenceEquals(_transport, transport))
                {
                    _transport = null;
                    _registered = false;
                }
            }

            transport.Dispose();
        }

        private void DetachAndDisposeTransportFireAndForget(ClientWebSocketTransport transport)
        {
            transport.PayloadReceived -= OnTransportPayload;
            transport.ErrorReceived -= OnTransportError;
            transport.Closed -= OnTransportClosed;

            Task.Run(() =>
            {
                try { transport.Dispose(); } catch { /* best effort */ }
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     The name of this websocket connection module
        /// </summary>
        public string Name
        {
            get => "ws-connection";
        }

        /// <summary>
        ///     The context string of this Websocket module
        /// </summary>
        public string Context
        {
            get => $"{_context}-{Name}";
        }

        private Task<ClientWebSocketTransport> Register(string url)
        {
            if (!Validation.IsWsUrl(url))
            {
                throw new ArgumentException("Provided URL is not compatible with WebSocket connection: " + url);
            }

            _logger.Log($"Register new WS connection. Is already connecting: {Connecting}");

            TaskCompletionSource<ClientWebSocketTransport> tcs;
            bool isFirst;
            ClientWebSocketTransport previousTransport = null;

            lock (_registerGate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(WebsocketConnection));
                }

                if (Connecting && _pendingRegister != null)
                {
                    tcs = _pendingRegister;
                    isFirst = false;
                }
                else
                {
                    if (_transport != null)
                    {
                        previousTransport = _transport;
                        _transport = null;
                        _registered = false;
                    }

                    Url = url;
                    Connecting = true;
                    _pendingRegister = new TaskCompletionSource<ClientWebSocketTransport>(TaskCreationOptions.RunContinuationsAsynchronously);
                    tcs = _pendingRegister;
                    isFirst = true;
                }
            }

            if (previousTransport != null)
            {
                DetachAndDisposeTransportFireAndForget(previousTransport);
            }

            if (!isFirst)
            {
                return tcs.Task;
            }

            return RegisterCore(url, tcs);
        }

        private async Task<ClientWebSocketTransport> RegisterCore(string url, TaskCompletionSource<ClientWebSocketTransport> tcs)
        {
            var transport = new ClientWebSocketTransport(new Uri(url), OpenTimeout, DefaultKeepAliveInterval, _logger);

            lock (_registerGate)
            {
                _pendingTransport = transport;
                _pendingTransportClosedEarly = false;
            }

            transport.PayloadReceived += OnTransportPayload;
            transport.ErrorReceived += OnTransportError;
            transport.Closed += OnTransportClosed;

            try
            {
                await transport.StartAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                lock (_registerGate)
                {
                    Connecting = false;
                    _pendingRegister = null;
                    if (ReferenceEquals(_pendingTransport, transport))
                    {
                        _pendingTransport = null;
                        _pendingTransportClosedEarly = false;
                    }
                }

                transport.PayloadReceived -= OnTransportPayload;
                transport.ErrorReceived -= OnTransportError;
                transport.Closed -= OnTransportClosed;
                transport.Dispose();

                var mapped = MapOpenException(e);
                RegisterErrored?.Invoke(this, mapped);
                Closed?.Invoke(this, EventArgs.Empty);
                tcs.TrySetException(mapped);

                if (!ReferenceEquals(mapped, e))
                    throw mapped;
                throw;
            }

            bool aborted;
            bool abortedByDispose;
            lock (_registerGate)
            {
                abortedByDispose = _disposed;
                aborted = abortedByDispose
                          || _pendingTransportClosedEarly
                          || transport.Faulted
                          || transport.ShutdownRequested;
                if (!aborted)
                {
                    _transport = transport;
                    _registered = true;
                }
                _pendingTransport = null;
                _pendingTransportClosedEarly = false;
                Connecting = false;
                _pendingRegister = null;
            }

            if (aborted)
            {
                transport.PayloadReceived -= OnTransportPayload;
                transport.ErrorReceived -= OnTransportError;
                transport.Closed -= OnTransportClosed;
                transport.Dispose();

                Exception abortException = abortedByDispose
                    ? (Exception)new ObjectDisposedException(nameof(WebsocketConnection))
                    : new IOException("WebSocket connection closed before registration completed.");
                tcs.TrySetException(abortException);
                throw abortException;
            }

            Opened?.Invoke(this, transport);
            tcs.TrySetResult(transport);
            return transport;
        }

        private void OnTransportPayload(object sender, string payload)
        {
            PayloadReceived?.Invoke(this, payload);
        }

        private void OnTransportError(object sender, Exception exception)
        {
            ErrorReceived?.Invoke(this, exception);
        }

        private void OnTransportClosed(object sender, EventArgs e)
        {
            var transport = sender as ClientWebSocketTransport;
            Connecting = false;

            // Three close paths:
            //   1. Client-initiated close (Close() / Dispose()): the calling code owns cleanup
            //      through its finally block, so we leave _transport alone here.
            //   2. Server-initiated graceful close: clear _transport so the next SendRequest
            //      auto-reopens.
            //   3. Transport faulted: leave _transport pointing at it so the next SendRequest's
            //      SendAsync throws and routes through OnError<T> as a JSON-RPC error envelope.
            if (transport != null && !transport.Faulted && !transport.ShutdownRequested)
            {
                transport.PayloadReceived -= OnTransportPayload;
                transport.ErrorReceived -= OnTransportError;
                transport.Closed -= OnTransportClosed;

                lock (_registerGate)
                {
                    if (_registered && ReferenceEquals(_transport, transport))
                    {
                        _transport = null;
                        _registered = false;
                    }
                    else if (ReferenceEquals(_pendingTransport, transport))
                    {
                        _pendingTransportClosedEarly = true;
                    }
                }

                // We're currently on the transport's receive-loop continuation, and
                // transport.Dispose() waits on _receiveLoop. Calling Dispose synchronously
                // here would self-deadlock for 1s, so schedule it off-thread. Without this
                // call the rented receive buffer and underlying ClientWebSocket would leak
                // on every server-initiated close.
                Task.Run(() =>
                {
                    try { transport.Dispose(); } catch { /* best effort */ }
                });
            }

            _logger.Log("Connection closed");
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private Exception MapOpenException(Exception e)
        {
            var current = e;
            while (current != null)
            {
                if (current is SocketException se)
                {
                    switch (se.SocketErrorCode)
                    {
                        case SocketError.HostNotFound:
                        case SocketError.NoData:
                        case SocketError.ConnectionRefused:
                        case SocketError.HostUnreachable:
                            return new IOException("Unavailable WS RPC url at " + Url);
                    }
                }
                current = current.InnerException;
            }
            return e;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                ClientWebSocketTransport transport;
                lock (_registerGate)
                {
                    if (_disposed)
                        return;
                    _disposed = true;
                    transport = _transport;
                    _transport = null;
                    _registered = false;
                }

                if (transport != null)
                {
                    transport.PayloadReceived -= OnTransportPayload;
                    transport.ErrorReceived -= OnTransportError;
                    transport.Closed -= OnTransportClosed;
                    transport.Dispose();
                }

                return;
            }

            _disposed = true;
        }

        private void OnError<T>(IJsonRpcPayload ogPayload, Exception e)
        {
            Exception underlying;
            if (e is ChannelClosedException cce)
            {
                underlying = cce.InnerException ?? new IOException("WebSocket connection closed.");
            }
            else
            {
                underlying = e;
            }

            var payload = new JsonRpcResponse<T>(ogPayload.Id,
                new Error
                {
                    Code = underlying.HResult,
                    Data = null,
                    Message = underlying.Message
                }, default);

            PayloadReceived?.Invoke(this, JsonConvert.SerializeObject(payload));
        }
    }
}
