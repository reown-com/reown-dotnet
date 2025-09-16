﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Crypto.Models;
using Reown.Core.Interfaces;
using Reown.Core.Models;
using Reown.Core.Models.History;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;

namespace Reown.Core.Controllers
{
    public class TypedMessageHandler : ITypedMessageHandler
    {
        private readonly Dictionary<string, DecodeOptions> _decodeOptionsMap = new();
        private readonly Dictionary<string, List<Func<MessageEvent, Task>>> _requestCallbacksMap = new();
        private readonly Queue<(string method, MessageEvent messageEvent)> _requestQueue = new();
        private readonly Dictionary<string, List<Action<MessageEvent>>> _responseCallbacksMap = new();
        private readonly HashSet<string> _typeSafeCache = new();
        private bool _initialized;
        private bool _isRequestQueueProcessing;
        protected bool Disposed;

        public TypedMessageHandler(ICoreClient coreClient)
        {
            CoreClient = coreClient;
        }

        public ICoreClient CoreClient { get; }

        /// <summary>
        ///     The name of this publisher module
        /// </summary>
        public string Name
        {
            get => $"{CoreClient.Name}-typedmessagehandler";
        }

        /// <summary>
        ///     The context string this publisher module is using
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     Build <see cref="PublishOptions" /> from an <see cref="RpcRequestOptionsAttribute" /> from
        ///     either the type T1 or T2. T1 will take priority over T2.
        /// </summary>
        /// <typeparam name="T1">The first type to check for <see cref="RpcRequestOptionsAttribute" /></typeparam>
        /// <typeparam name="T2">The second type to check for <see cref="RpcRequestOptionsAttribute" /></typeparam>
        /// <returns>
        ///     <see cref="PublishOptions" /> constructed from the values found in the <see cref="RpcRequestOptionsAttribute" />
        ///     from either type T1 or T2
        /// </returns>
        /// <exception cref="InvalidOperationException">If no <see cref="RpcOptionsAttribute" /> is found in either type</exception>
        public PublishOptions RpcRequestOptionsFromType<T1, T2>()
        {
            var opts = RpcRequestOptionsForType<T1>();
            if (opts == null)
            {
                opts = RpcRequestOptionsForType<T2>();
                if (opts == null)
                {
                    throw new InvalidOperationException(
                        $"No RpcRequestOptions attribute found on either {typeof(T1).FullName} or {typeof(T2).FullName}. "
                        + $"Ensure that at least one of these types is decorated with the {nameof(RpcRequestOptionsAttribute)}."
                    );
                }
            }

            return opts;
        }

        /// <summary>
        ///     Build <see cref="PublishOptions" /> from an <see cref="RpcRequestOptionsAttribute" /> from
        ///     the given type T
        /// </summary>
        /// <typeparam name="T">The type to check for <see cref="RpcRequestOptionsAttribute" /></typeparam>
        /// <returns>
        ///     <see cref="PublishOptions" /> constructed from the values found in the <see cref="RpcRequestOptionsAttribute" />
        ///     from the given type T
        /// </returns>
        /// <exception cref="InvalidOperationException">If no <see cref="RpcOptionsAttribute" /> is found in the type T or if multiple are found</exception>
        public PublishOptions RpcRequestOptionsForType<T>()
        {
            var attributes = typeof(T).GetCustomAttributes(typeof(RpcRequestOptionsAttribute), true);
            switch (attributes.Length)
            {
                case 0:
                    throw new InvalidOperationException($"Type {typeof(T).FullName} has no {nameof(RpcRequestOptionsAttribute)} defined.");
                case > 1:
                    throw new InvalidOperationException($"Type {typeof(T).FullName} has multiple {nameof(RpcRequestOptionsAttribute)} definitions. Only one is allowed.");
            }

            var opts = attributes.Cast<RpcRequestOptionsAttribute>().SingleOrDefault();
            if (opts == null)
            {
                throw new InvalidOperationException($"Type {typeof(T).FullName} has multiple {nameof(RpcRequestOptionsAttribute)} definitions. Only one is allowed.");
            }

            return new PublishOptions
            {
                Tag = opts.Tag,
                TTL = opts.TTL
            };
        }

        /// <summary>
        ///     Build <see cref="PublishOptions" /> from an <see cref="RpcResponseOptionsAttribute" /> from
        ///     either the type T1 or T2. T1 will take priority over T2.
        /// </summary>
        /// <typeparam name="T1">The first type to check for <see cref="RpcResponseOptionsAttribute" /></typeparam>
        /// <typeparam name="T2">The second type to check for <see cref="RpcResponseOptionsAttribute" /></typeparam>
        /// <returns>
        ///     <see cref="PublishOptions" /> constructed from the values found in the <see cref="RpcResponseOptionsAttribute" />
        ///     from either type T1 or T2
        /// </returns>
        /// <exception cref="InvalidOperationException">If no <see cref="RpcResponseOptionsAttribute" /> is found in either type</exception>
        public PublishOptions RpcResponseOptionsFromTypes<T1, T2>()
        {
            var opts = RpcResponseOptionsForType<T1>() ?? RpcResponseOptionsForType<T2>();
            if (opts == null)
            {
                throw new InvalidOperationException(
                    $"No {nameof(RpcResponseOptionsAttribute)} found on either {typeof(T1).FullName} or {typeof(T2).FullName}. " +
                    "Ensure that at least one of these types is decorated with the RpcResponseOptionsAttribute.");
            }

            return opts;
        }

        /// <summary>
        ///     Build <see cref="PublishOptions" /> from an <see cref="RpcResponseOptionsAttribute" /> from
        ///     the given type T
        /// </summary>
        /// <typeparam name="T">The type to check for <see cref="RpcResponseOptionsAttribute" /></typeparam>
        /// <returns>
        ///     <see cref="PublishOptions" /> constructed from the values found in the <see cref="RpcResponseOptionsAttribute" />
        ///     from the given type T
        /// </returns>
        /// <exception cref="Exception">If no <see cref="RpcResponseOptionsAttribute" /> is found in the type T</exception>
        public PublishOptions RpcResponseOptionsForType<T>()
        {
            var attributes = typeof(T).GetCustomAttributes(typeof(RpcResponseOptionsAttribute), true);
            switch (attributes.Length)
            {
                case 0:
                    return null;
                case > 1:
                    throw new InvalidOperationException($"Type {typeof(T).FullName} has multiple {nameof(RpcMethodAttribute)} definitions. Only one is allowed.");
            }

            var opts = attributes.Cast<RpcResponseOptionsAttribute>().SingleOrDefault();
            if (opts == null)
            {
                throw new InvalidOperationException($"Type {typeof(T).FullName} has multiple {nameof(RpcMethodAttribute)} definitions. Only one is allowed.");
            }

            return new PublishOptions
            {
                Tag = opts.Tag,
                TTL = opts.TTL
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event EventHandler<DecodedMessageEvent> RawMessage;

        public Task Init()
        {
            if (!_initialized)
            {
                CoreClient.Relayer.OnMessageReceived += RelayMessageCallback;
            }

            _initialized = true;
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Handle a specific request / response type and call the given callbacks for requests and responses. The
        ///     response callback is only triggered when it originates from the request of the same type.
        /// </summary>
        /// <param name="requestCallback">The callback function to invoke when a request is received with the given request type</param>
        /// <param name="responseCallback">The callback function to invoke when a response is received with the given response type</param>
        /// <typeparam name="T">The request type to trigger the requestCallback for</typeparam>
        /// <typeparam name="TR">The response type to trigger the responseCallback for</typeparam>
        public async Task<DisposeHandlerToken> HandleMessageType<T, TR>(Func<string, JsonRpcRequest<T>, Task> requestCallback,
            Func<string, JsonRpcResponse<TR>, Task> responseCallback)
        {
            var method = RpcMethodAttribute.MethodForType<T>();
            var rpcHistory = await CoreClient.History.JsonRpcHistoryOfType<T, TR>();

            async Task RequestCallback(MessageEvent e)
            {
                try
                {
                    if (requestCallback == null || Disposed)
                    {
                        return;
                    }

                    var topic = e.Topic;
                    var message = e.Message;

                    var options = DecodeOptionForTopic(topic);

                    if (options == null && !await CoreClient.Crypto.HasKeys(topic))
                    {
                        return;
                    }

                    var payload = await CoreClient.Crypto.Decode<JsonRpcRequest<T>>(topic, message, options);

                    (await CoreClient.History.JsonRpcHistoryOfType<T, TR>()).Set(topic, payload, null);

                    await requestCallback(topic, payload);
                }
                catch (JsonException)
                {
                }
            }

            async void ResponseCallback(MessageEvent e)
            {
                if (responseCallback == null || Disposed)
                {
                    return;
                }

                var topic = e.Topic;
                var message = e.Message;

                var options = DecodeOptionForTopic(topic);

                if (options == null && !await CoreClient.Crypto.HasKeys(topic)) return;

                var rawResultPayload = await CoreClient.Crypto.Decode<JsonRpcPayload>(topic, message, options);

                var history = await CoreClient.History.JsonRpcHistoryOfType<T, TR>();
                var expectingResult = await history.Exists(topic, rawResultPayload.Id);

                try
                {
                    var payload = await CoreClient.Crypto.Decode<JsonRpcResponse<TR>>(topic, message, options);

                    await history.Resolve(payload);

                    await responseCallback(topic, payload);
                }
                catch (Exception ex) when (ex is JsonException)
                {
                    if (!expectingResult)
                        return;
                    throw;
                }
            }

            async void InspectResponseRaw(object sender, DecodedMessageEvent e)
            {
                var topic = e.Topic;
                var message = e.Message;

                var payload = e.Payload;

                JsonRpcRecord<T, TR> record;
                try
                {
                    record = await rpcHistory.Get(topic, payload.Id);
                }
                catch (KeyNotFoundException)
                {
                    // Ignored if we can't find anything in the history
                    return;
                }

                var resMethod = record.Request.Method;

                // Trigger the true response event, which will trigger ResponseCallback

                if (_responseCallbacksMap.TryGetValue(resMethod, out var callbacks))
                {
                    var callbacksCopy = callbacks.ToList();
                    foreach (var callback in callbacksCopy)
                    {
                        callback(new MessageEvent
                        {
                            Topic = topic,
                            Message = message
                        });
                    }
                }
            }

            if (!_requestCallbacksMap.TryGetValue(method, out var requestCallbacks))
            {
                requestCallbacks = new List<Func<MessageEvent, Task>>();
                _requestCallbacksMap.Add(method, requestCallbacks);
            }

            requestCallbacks.Add(RequestCallback);


            if (!_responseCallbacksMap.TryGetValue(method, out var responseCallbacks))
            {
                responseCallbacks = new List<Action<MessageEvent>>();
                _responseCallbacksMap.Add(method, responseCallbacks);
            }

            responseCallbacks.Add(ResponseCallback);

            // Handle response_raw in this context
            // This will allow us to examine response_raw in every typed context registered
            RawMessage += InspectResponseRaw;

            return new DisposeHandlerToken(() =>
            {
                RawMessage -= InspectResponseRaw;

                _requestCallbacksMap[method].Remove(RequestCallback);
                _responseCallbacksMap[method].Remove(ResponseCallback);
            });
        }

        public void SetDecodeOptionsForTopic(DecodeOptions options, string topic)
        {
            _decodeOptionsMap.Add(topic, options);
        }

        public DecodeOptions DecodeOptionForTopic(string topic)
        {
            return _decodeOptionsMap.GetValueOrDefault(topic);
        }

        /// <summary>
        ///     Send a typed request message with the given request / response type pair T, TR to the given topic
        /// </summary>
        /// <param name="topic">The topic to send the request in</param>
        /// <param name="parameters">The typed request message to send</param>
        /// <param name="expiry">
        ///     An override to specify how long this request will live for. If null is given, then expiry will be taken from either T or TR
        ///     attributed options
        /// </param>
        /// <typeparam name="T">The request type</typeparam>
        /// <typeparam name="TR">The response type</typeparam>
        /// <returns>The id of the request sent</returns>
        public async Task<long> SendRequest<T, TR>(string topic, T parameters, long? expiry = null,
            EncodeOptions options = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            EnsureTypeIsSerializerSafe(parameters);

            var method = RpcMethodAttribute.MethodForType<T>();

            var messageId = RpcPayloadId.GenerateFromDataHash(parameters);

            var payload = new JsonRpcRequest<T>(method, parameters, messageId);

            var message = await CoreClient.Crypto.Encode(topic, payload, options);

            var opts = RpcRequestOptionsFromType<T, TR>();

            if (expiry != null)
            {
                opts.TTL = (long)expiry;
            }

            (await CoreClient.History.JsonRpcHistoryOfType<T, TR>()).Set(topic, payload, null);

            await CoreClient.Relayer.Publish(topic, message, opts);

            return payload.Id;
        }

        /// <summary>
        ///     Send a typed response message with the given request / response type pair T, TR to the given topic
        /// </summary>
        /// <param name="id">The id of the request to respond to</param>
        /// <param name="topic">The topic to send the response in</param>
        /// <param name="result">The typed response message to send</param>
        /// <typeparam name="T">The request type</typeparam>
        /// <typeparam name="TR">The response type</typeparam>
        public async Task SendResult<T, TR>(long id, string topic, TR result, EncodeOptions options = null)
        {
            EnsureTypeIsSerializerSafe(result);

            var payload = new JsonRpcResponse<TR>(id, null, result);
            var message = await CoreClient.Crypto.Encode(topic, payload, options);
            var opts = RpcResponseOptionsFromTypes<T, TR>();
            await CoreClient.Relayer.Publish(topic, message, opts);
            await (await CoreClient.History.JsonRpcHistoryOfType<T, TR>()).Resolve(payload);
        }

        /// <summary>
        ///     Send an error response message with the given request / response type pair T, TR to the given topic
        /// </summary>
        /// <param name="id">The id of the request to respond to</param>
        /// <param name="topic">The topic to send the response in</param>
        /// <param name="error">The error response to send</param>
        /// <typeparam name="T">The request type</typeparam>
        /// <typeparam name="TR">The response type</typeparam>
        public async Task SendError<T, TR>(long id, string topic, Error error, EncodeOptions options = null)
        {
            var payload = new JsonRpcResponse<TR>(id, error, default);
            var message = await CoreClient.Crypto.Encode(topic, payload, options);
            var opts = RpcResponseOptionsFromTypes<T, TR>();
            await CoreClient.Relayer.Publish(topic, message, opts);
            await (await CoreClient.History.JsonRpcHistoryOfType<T, TR>()).Resolve(payload);
        }

        private async void RelayMessageCallback(object sender, MessageEvent e)
        {
            var topic = e.Topic;
            var message = e.Message;

            var options = DecodeOptionForTopic(topic);

            var payload = await CoreClient.Crypto.Decode<JsonRpcPayload>(topic, message, options);
            if (payload.IsRequest)
            {
                _requestQueue.Enqueue((payload.Method, e));
                await ProcessRequestQueue();
            }
            else if (payload.IsResponse)
            {
                RawMessage?.Invoke(this,
                    new DecodedMessageEvent
                    {
                        Topic = topic,
                        Message = message,
                        Payload = payload
                    });
            }
        }

        private async Task ProcessRequestQueue()
        {
            if (_isRequestQueueProcessing)
            {
                return;
            }

            _isRequestQueueProcessing = true;

            try
            {
                while (_requestQueue.Count > 0)
                {
                    var (method, messageEvent) = _requestQueue.Dequeue();
                    if (!_requestCallbacksMap.TryGetValue(method, out var callbacks))
                    {
                        continue;
                    }

                    for (var i = callbacks.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            await callbacks[i](messageEvent);
                        }
                        catch (Exception e)
                        {
                            ReownLogger.LogError(e);
                        }
                    }
                }
            }
            finally
            {
                _isRequestQueueProcessing = false;
            }
        }

        private void EnsureTypeIsSerializerSafe<T>(T testObject)
        {
            var typeString = typeof(T).FullName;
            if (_typeSafeCache.Contains(typeString))
                return;

            // Throw any serialization exceptions now
            // before it's too late
            TypeSafety.EnsureTypeSerializerSafe(testObject);

            _typeSafeCache.Add(typeString);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed)
                return;

            if (disposing)
            {
                CoreClient.Relayer.OnMessageReceived -= RelayMessageCallback;
            }

            Disposed = true;
        }
    }
}