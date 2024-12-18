using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common.Events;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Network.Models;

namespace Reown.Core.Network
{
    /// <summary>
    ///     A full implementation of the IJsonRpcProvider interface using the EventDelegator
    /// </summary>
    public class JsonRpcProvider : IJsonRpcProvider
    {
        private readonly string _context;
        private readonly GenericEventHolder _jsonResponseEventHolder = new();
        private readonly ILogger _logger;
        private TaskCompletionSource<bool> _connecting = new();
        private bool _connectingStarted;
        private bool _hasRegisteredEventListeners;
        private long _lastId;
        protected bool Disposed;

        /// <summary>
        ///     Create a new JsonRpcProvider with the given connection
        /// </summary>
        /// <param name="connection">The IJsonRpcConnection to use</param>
        public JsonRpcProvider(IJsonRpcConnection connection, string context = null)
        {
            _context = context ?? Guid.NewGuid().ToString();
            _logger = ReownLogger.WithContext(Context);

            Connection = connection;
            if (Connection.Connected)
            {
                RegisterEventListeners();
            }
        }

        /// <summary>
        ///     Whether the provider is currently connecting or not
        /// </summary>
        public bool IsConnecting
        {
            get => _connectingStarted && !_connecting.Task.IsCompleted;
        }

        /// <summary>
        ///     The name of this provider module
        /// </summary>
        public string Name
        {
            get => "json-rpc-provider";
        }

        /// <summary>
        ///     The context string of this provider module
        /// </summary>
        public string Context
        {
            get => $"{_context}-{Name}";
        }

        /// <summary>
        ///     The current Connection for this provider
        /// </summary>
        public IJsonRpcConnection Connection { get; private set; }

        public event EventHandler<JsonRpcPayload> PayloadReceived;

        public event EventHandler<IJsonRpcConnection> Connected;

        public event EventHandler Disconnected;

        public event EventHandler<Exception> ErrorReceived;

        public event EventHandler<string> RawMessageReceived;

        /// <summary>
        ///     Connect this provider using the given connection string
        /// </summary>
        /// <param name="connection">The connection string to use to connect, usually a URI</param>
        public async Task Connect(string connection)
        {
            if (Connection.Connected)
            {
                await Connection.Close();
            }

            // Reset connecting task
            _connecting = new TaskCompletionSource<bool>();
            _connectingStarted = true;

            await Connection.Open(connection);

            FinalizeConnection(Connection);
        }

        /// <summary>
        ///     Connect this provider with the given IJsonRpcConnection connection
        /// </summary>
        /// <param name="connection">The connection object to use to connect</param>
        public async Task Connect(IJsonRpcConnection connection)
        {
            if (Connection.Url == connection.Url && connection.Connected) return;
            if (Connection.Connected)
            {
                await Connection.Close();
            }

            // Reset connecting task
            _connecting = new TaskCompletionSource<bool>();
            _connectingStarted = true;

            void OnConnectionOnOpened(object o, object o1)
            {
                _connecting.SetResult(true);
            }

            void OnConnectionErrored(object o, Exception e)
            {
                if (_connecting.Task.IsCompleted)
                {
                    return;
                }

                _connecting.SetException(e);
            }

            connection.Opened += OnConnectionOnOpened;
            connection.ErrorReceived += OnConnectionErrored;

            await connection.Open();

            try
            {
                await _connecting.Task;
            }
            catch (Exception)
            {
                _connectingStarted = false;
                throw;
            }
            finally
            {
                connection.Opened -= OnConnectionOnOpened;
                connection.ErrorReceived -= OnConnectionErrored;
            }

            FinalizeConnection(connection);
        }

        /// <summary>
        ///     Connect this provider using the backing IJsonRpcConnection that was set in the
        ///     constructor
        /// </summary>
        public async Task Connect()
        {
            if (Connection == null)
                throw new InvalidOperationException("Connection is null");

            await Connect(Connection);
        }

        /// <summary>
        ///     Disconnect this provider
        /// </summary>
        public async Task Disconnect()
        {
            await Connection.Close();
            // Reset connecting task
            _connecting = new TaskCompletionSource<bool>();
        }

        /// <summary>
        ///     Send a request and wait for a response. The response is returned as a task and can
        ///     be awaited
        /// </summary>
        /// <param name="requestArgs">The request arguments to send</param>
        /// <param name="context">The context to use during sending</param>
        /// <typeparam name="T">The type of request arguments</typeparam>
        /// <typeparam name="TR">The type of the response</typeparam>
        /// <returns>A Task that will resolve when a response is received</returns>
        public async Task<TR> Request<T, TR>(IRequestArguments<T> requestArgs, object context = null)
        {
            _logger.Log("Checking if connected");
            if (IsConnecting)
            {
                await _connecting.Task;
            }
            else if (!_connectingStarted && !Connection.Connected)
            {
                _logger.Log("Not connected, connecting now");
                await Connect(Connection);
            }

            long? id = null;
            if (requestArgs is IJsonRpcRequest<T>)
            {
                id = ((IJsonRpcRequest<T>)requestArgs).Id;
                if (id == 0)
                    id = null; // An id of 0 is null
            }

            var request = new JsonRpcRequest<T>(requestArgs.Method, requestArgs.Params, id);

            var requestTask = new TaskCompletionSource<TR>(TaskCreationOptions.None);

            _jsonResponseEventHolder.OfType<string>()[request.Id.ToString()] += (sender, responseJson) =>
            {
                if (requestTask.Task.IsCompleted)
                    return;

                var result = JsonConvert.DeserializeObject<JsonRpcResponse<TR>>(responseJson);

                if (result.Error != null)
                {
                    requestTask.SetException(new IOException(result.Error.Message));
                }
                else
                {
                    requestTask.SetResult(result.Result);
                }
            };

            _jsonResponseEventHolder.OfType<ReownNetworkException>()[request.Id.ToString()] += (sender, exception) =>
            {
                if (requestTask.Task.IsCompleted)
                    return;

                if (exception != null)
                {
                    requestTask.SetException(exception);
                }
            };

            Connection.ErrorReceived += (_, exception) =>
            {
                if (requestTask.Task.IsCompleted)
                {
                    return;
                }

                requestTask.SetException(exception);
            };

            _lastId = request.Id;

            _logger.Log($"Sending request {request.Method} with data {JsonConvert.SerializeObject(request)}");
            await Connection.SendRequest(request, context);

            await requestTask.Task;

            return requestTask.Task.Result;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void FinalizeConnection(IJsonRpcConnection connection)
        {
            Connection = connection;
            RegisterEventListeners();
            Connected?.Invoke(this, connection);
            _connectingStarted = false;
        }

        protected void RegisterEventListeners()
        {
            if (_hasRegisteredEventListeners) return;

            Connection.PayloadReceived += OnPayload;
            Connection.Closed += OnConnectionDisconnected;
            Connection.ErrorReceived += OnConnectionError;

            _hasRegisteredEventListeners = true;
        }

        protected void UnregisterEventListeners()
        {
            if (!_hasRegisteredEventListeners) return;

            Connection.PayloadReceived -= OnPayload;
            Connection.Closed -= OnConnectionDisconnected;
            Connection.ErrorReceived -= OnConnectionError;

            _hasRegisteredEventListeners = false;
        }

        private void OnConnectionError(object sender, Exception e)
        {
            ErrorReceived?.Invoke(this, e);
        }

        private void OnConnectionDisconnected(object sender, EventArgs e)
        {
            Disconnected?.Invoke(this, e);
        }

        private void OnPayload(object sender, string json)
        {
            var payload = JsonConvert.DeserializeObject<JsonRpcPayload>(json);

            if (payload == null)
            {
                throw new IOException("Invalid payload: " + json);
            }

            if (payload.Id == 0)
                payload.Id = _lastId;

            PayloadReceived?.Invoke(this, payload);

            if (payload.IsRequest)
            {
                RawMessageReceived?.Invoke(this, json);
            }
            else
            {
                if (payload.IsError)
                {
                    var errorPayload = JsonConvert.DeserializeObject<JsonRpcError>(json);
                    _jsonResponseEventHolder.OfType<ReownNetworkException>()[payload.Id.ToString()](this,
                        errorPayload.Error.ToException());
                }
                else
                {
                    _jsonResponseEventHolder.OfType<string>()[payload.Id.ToString()](this, json);
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                UnregisterEventListeners();
                Connection?.Dispose();
            }

            Disposed = true;
        }
    }
}