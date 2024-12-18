using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Websocket.Client;

namespace Reown.Core.Network.Websocket
{
    /// <summary>
    ///     A JSON RPC connection using Websocket.Client library + EventDelegator
    /// </summary>
    public class WebsocketConnection : IJsonRpcConnection, IModule
    {
        private const string AddressNotFoundError = "getaddrinfo ENOTFOUND";
        private const string ConnectionRefusedError = "connect ECONNREFUSED";
        private readonly string _context;
        private readonly ILogger _logger;
        private WebsocketClient _socket;
        private IDisposable _messageSubscription;
        private IDisposable _disconnectionSubscription;
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
            get => _socket is { NativeClient: { State: WebSocketState.Open } };
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
            if (_socket == null)
                throw new IOException("Connection already closed");

            await _socket.Stop(WebSocketCloseStatus.NormalClosure, "Close Invoked");

            OnClose(new DisconnectionInfo(DisconnectionType.Exit, WebSocketCloseStatus.Empty, "Close Invoked", null,
                null));
        }

        /// <summary>
        ///     Send a Json RPC request through this websocket connection, using the given context
        /// </summary>
        /// <param name="requestPayload">The request payload to encode and send</param>
        /// <param name="context">The context to use when sending</param>
        /// <typeparam name="T">The type of the Json RPC request parameter</typeparam>
        public async Task SendRequest<T>(IJsonRpcRequest<T> requestPayload, object context)
        {
            _socket ??= await Register(Url);

            try
            {
                _logger.Log("Sending request over websocket");
                _socket.Send(JsonConvert.SerializeObject(requestPayload));
            }
            catch (Exception e)
            {
                _logger.Log($"Error sending request: {e.Message}");
                OnError<T>(requestPayload, e);
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
            _socket ??= await Register(Url);

            try
            {
                _socket.Send(JsonConvert.SerializeObject(responsePayload));
            }
            catch (Exception e)
            {
                OnError<T>(responsePayload, e);
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
            _socket ??= await Register(Url);

            try
            {
                _socket.Send(JsonConvert.SerializeObject(errorPayload));
            }
            catch (Exception e)
            {
                OnError<object>(errorPayload, e);
            }
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

        private async Task<WebsocketClient> Register(string url)
        {
            if (!Validation.IsWsUrl(url))
            {
                throw new ArgumentException("Provided URL is not compatible with WebSocket connection: " + url);
            }

            _logger.Log($"Register new WS connection. Is already connecting: {Connecting}");

            if (Connecting)
            {
                var registeringTask = new TaskCompletionSource<WebsocketClient>(TaskCreationOptions.None);

                RegisterErrored.ListenOnce((sender, args) => registeringTask.SetException(args));
                Opened.ListenOnce((sender, args) => registeringTask.SetResult((WebsocketClient)args));

                await registeringTask.Task;

                return registeringTask.Task.Result;
            }

            Url = url;
            Connecting = true;

            try
            {
                _socket = new WebsocketClient(new Uri(Url));
                _socket.ReconnectTimeout = null;

                await _socket.Start().WithTimeout(OpenTimeout, "Unavailable WS RPC url at " + Url);
                OnOpen(_socket);
                return _socket;
            }
            catch (Exception e)
            {
                RegisterErrored?.Invoke(this, e);
                OnClose(new DisconnectionInfo(DisconnectionType.Error, WebSocketCloseStatus.Empty, e.Message, null, e));

                throw;
            }
        }

        private void OnOpen(WebsocketClient socket)
        {
            if (socket == null)
                return;

            _messageSubscription = socket.MessageReceived.Subscribe(OnPayload);
            _disconnectionSubscription = socket.DisconnectionHappened.Subscribe(OnDisconnect);

            _socket = socket;
            Connecting = false;
            Opened?.Invoke(this, _socket);
        }

        private void OnDisconnect(DisconnectionInfo obj)
        {
            if (obj.Exception != null)
                ErrorReceived?.Invoke(this, obj.Exception);

            OnClose(obj);
        }

        private void OnClose(DisconnectionInfo obj)
        {
            if (_socket == null)
                return;

            _logger.Log($"Connection closed. Close status: {obj.CloseStatus?.ToString() ?? "-- "}. Exception message: {obj.Exception?.Message ?? "--"}");

            _socket = null;
            Connecting = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void OnPayload(ResponseMessage obj)
        {
            string json = null;
            switch (obj.MessageType)
            {
                case WebSocketMessageType.Binary:
                    return;
                case WebSocketMessageType.Text:
                    json = obj.Text;
                    break;
                case WebSocketMessageType.Close:
                    return;
            }

            if (string.IsNullOrWhiteSpace(json))
                return;

            PayloadReceived?.Invoke(this, json);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _messageSubscription?.Dispose();
                _disconnectionSubscription?.Dispose();

                if (_socket != null)
                {
                    _socket.Dispose();
                    _socket = null;
                }
            }

            _disposed = true;
        }

        private void OnError<T>(IJsonRpcPayload ogPayload, Exception e)
        {
            var exception = e.Message.Contains(AddressNotFoundError) || e.Message.Contains(ConnectionRefusedError)
                ? new IOException("Unavailable WS RPC url at " + Url)
                : e;

            var message = exception.Message;
            var payload = new JsonRpcResponse<T>(ogPayload.Id,
                new Error
                {
                    Code = exception.HResult,
                    Data = null,
                    Message = message
                }, default);

            //Trigger the payload event, converting the new JsonRpcResponse object to JSON string
            PayloadReceived?.Invoke(this, JsonConvert.SerializeObject(payload));
        }
    }
}