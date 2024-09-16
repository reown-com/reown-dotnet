using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models.Verify;
using Reown.Core.Network.Models;

namespace Reown.Core.Models.MessageHandler
{
    /// A class that helps handle storing static event handlers of type T, TR based on some filter
    /// predicate for requests and responses. This acts as a singleton-per-context, which means that each
    /// <see cref="IEngine" />
    /// instance has their own singleton instance of this class that can be accessed
    /// by invoking
    /// <see cref="GetInstance(IEngine)" />
    /// </summary>
    /// <typeparam name="T">The request type to filter for</typeparam>
    /// <typeparam name="TR">The response typ to filter for</typeparam>
    public class TypedEventHandler<T, TR> : IDisposable
    {
        /// <summary>
        ///     The callback function delegate that handles requests of the type TRequestArgs, TResponseArgs. These
        ///     functions are async and return a Task.
        /// </summary>
        /// <typeparam name="TRequestArgs">The type of the request this function is for</typeparam>
        /// <typeparam name="TResponseArgs">The type of the response this function is for</typeparam>
        public delegate Task RequestMethod<TRequestArgs, TResponseArgs>(RequestEventArgs<TRequestArgs, TResponseArgs> e);

        /// <summary>
        ///     The callback function delegate that handles responses of the type TResponseArgs. These
        ///     functions are async and return a Task.
        /// </summary>
        /// <typeparam name="TResponseArgs">The type of the response this function is for</typeparam>
        public delegate Task ResponseMethod<TResponseArgs>(ResponseEventArgs<TResponseArgs> e);

        protected static readonly Dictionary<string, TypedEventHandler<T, TR>> Instances = new();
        private readonly object _eventLock = new();
        protected readonly List<Action> DisposeActions = new();
        protected readonly ICoreClient Ref;
        private int _activeCount;

        protected DisposeHandlerToken MessageHandler;
        protected Func<RequestEventArgs<T, TR>, bool> RequestPredicate;
        protected Func<ResponseEventArgs<TR>, bool> ResponsePredicate;

        protected TypedEventHandler(ICoreClient engine)
        {
            Ref = engine;
        }

        public bool Disposed { get; protected set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        private event RequestMethod<T, TR> _onRequest;
        private event ResponseMethod<TR> _onResponse;

        /// <summary>
        ///     Get a singleton instance of this class for the given <see cref="IEngine" /> context. The context
        ///     string of the given <see cref="IEngine" /> will be used to determine the singleton instance to
        ///     return (or if a new one needs to be created). Beware that multiple <see cref="IEngine" /> instances
        ///     with the same context string will share the same event handlers.
        /// </summary>
        /// <param name="engine">
        ///     The engine this singleton instance is for, and where the context string will
        ///     be read from
        /// </param>
        /// <returns>The singleton instance to use for request/response event handlers</returns>
        public static TypedEventHandler<T, TR> GetInstance(ICoreClient engine)
        {
            var context = engine.Context;

            if (Instances.TryGetValue(context, out var instance))
                return instance;

            var newInstance = new TypedEventHandler<T, TR>(engine);

            Instances.Add(context, newInstance);

            return newInstance;
        }

        /// <summary>
        ///     The event handler that triggers when a new request of type
        ///     T, TR is received. This event handler is only triggered
        ///     if the predicate given from <see cref="FilterRequests" /> is satisfied. If no
        ///     predicate was given, then this will always fire for the type T, TR
        /// </summary>
        public event RequestMethod<T, TR> OnRequest
        {
            add
            {
                lock (_eventLock)
                {
                    _onRequest += value;

                    if (_activeCount == 0)
                    {
                        Setup();
                    }

                    _activeCount++;
                }
            }
            remove
            {
                lock (_eventLock)
                {
                    _onRequest -= value;

                    _activeCount--;

                    if (_activeCount == 0)
                    {
                        Teardown();
                    }
                }
            }
        }

        /// <summary>
        ///     The event handler that triggers when a new response of type
        ///     TR is received. This event handler is only triggered
        ///     if the predicate given from <see cref="FilterResponses" /> is satisfied. If no
        ///     predicate was given, then this will always fire for the type TR
        /// </summary>
        public event ResponseMethod<TR> OnResponse
        {
            add
            {
                lock (_eventLock)
                {
                    _onResponse += value;

                    if (_activeCount == 0)
                    {
                        Setup();
                    }

                    _activeCount++;
                }
            }
            remove
            {
                lock (_eventLock)
                {
                    _onResponse -= value;

                    _activeCount--;

                    if (_activeCount == 0)
                    {
                        Teardown();
                    }
                }
            }
        }

        /// <summary>
        ///     Filter request events based on the given predicate. This will return a new instance of this
        ///     <see cref="TypedEventHandler{T,TR}" /> that will only fire the <see cref="OnRequest" /> event handler
        ///     if the given predicate is satisfied. The event firing of <see cref="OnResponse" /> is unaffected.
        /// </summary>
        /// <param name="predicate">The predicate that must be satisfied for <see cref="OnRequest" /> to fire</param>
        /// <returns>
        ///     A new instance of <see cref="TypedEventHandler{T,TR}" /> that will filter <see cref="OnRequest" /> event
        ///     firing based on the given predicate
        /// </returns>
        public virtual TypedEventHandler<T, TR> FilterRequests(Func<RequestEventArgs<T, TR>, bool> predicate)
        {
            var finalPredicate = predicate;
            if (RequestPredicate != null)
                finalPredicate = rea => RequestPredicate(rea) && predicate(rea);

            return BuildNew(Ref, finalPredicate, ResponsePredicate);
        }

        /// <summary>
        ///     Filter response events based on the given predicate. This will return a new instance of this
        ///     <see cref="TypedEventHandler{T,TR}" /> that will only fire the <see cref="OnResponse" /> event handler
        ///     if the given predicate is satisfied. The event firing of <see cref="OnRequest" /> is unaffected.
        /// </summary>
        /// <param name="predicate">The predicate that must be satisfied for <see cref="OnResponse" /> to fire</param>
        /// <returns>
        ///     A new instance of <see cref="TypedEventHandler{T,TR}" /> that will filter <see cref="OnResponse" /> event
        ///     firing based on the given predicate
        /// </returns>
        public virtual TypedEventHandler<T, TR> FilterResponses(Func<ResponseEventArgs<TR>, bool> predicate)
        {
            var finalPredicate = predicate;
            if (ResponsePredicate != null)
                finalPredicate = rea => ResponsePredicate(rea) && predicate(rea);

            return BuildNew(Ref, RequestPredicate, finalPredicate);
        }

        protected virtual TypedEventHandler<T, TR> BuildNew(ICoreClient _ref,
            Func<RequestEventArgs<T, TR>, bool> requestPredicate,
            Func<ResponseEventArgs<TR>, bool> responsePredicate)
        {
            var wrappedRef = new TypedEventHandler<T, TR>(_ref)
            {
                RequestPredicate = requestPredicate,
                ResponsePredicate = responsePredicate
            };

            DisposeActions.Add(wrappedRef.Dispose);

            return wrappedRef;
        }

        protected virtual async void Setup()
        {
            MessageHandler = await Ref.MessageHandler.HandleMessageType<T, TR>(RequestCallback, ResponseCallback);
        }

        protected virtual void Teardown()
        {
            if (MessageHandler != null)
            {
                MessageHandler.Dispose();
                MessageHandler = null;
            }
        }

        protected virtual Task ResponseCallback(string arg1, JsonRpcResponse<TR> arg2)
        {
            var rea = new ResponseEventArgs<TR>(arg2, arg1);
            return ResponsePredicate != null && !ResponsePredicate(rea) ? Task.CompletedTask :
                _onResponse != null ? _onResponse(rea) : Task.CompletedTask;
        }

        protected virtual async Task RequestCallback(string arg1, JsonRpcRequest<T> arg2)
        {
            var verifyContext = new VerifiedContext
            {
                Validation = Validation.Unknown
            };

            // Find pairing to get metadata
            if (Ref.Pairing.Store.Keys.Contains(arg1))
            {
                var pairing = Ref.Pairing.Store.Get(arg1);

                var hash = HashUtils.HashMessage(JsonConvert.SerializeObject(arg2));
                verifyContext = await VerifyContext(hash, pairing.PeerMetadata);
            }

            var rea = new RequestEventArgs<T, TR>(arg1, arg2, verifyContext);

            if (RequestPredicate != null && !RequestPredicate(rea)) return;
            if (_onRequest == null) return;

            var isDisposed = ((CoreClient)Ref).Disposed;

            if (isDisposed)
            {
                ReownLogger.Log($"Too late to process request {typeof(T)} in topic {arg1}, the Reown.Core instance {Ref.Context} was disposed before we could");
                return;
            }

            await _onRequest(rea);

            var nextIsDisposed = ((CoreClient)Ref).Disposed;

            if (nextIsDisposed)
            {
                ReownLogger.Log($"Too late to send a result for request {typeof(T)} in topic {arg1}, the Reown.Core instance {Ref.Context} was disposed before we could");
                return;
            }

            if (rea.Error != null)
            {
                await Ref.MessageHandler.SendError<T, TR>(arg2.Id, arg1, rea.Error);
            }
            else if (rea.Response != null)
            {
                await Ref.MessageHandler.SendResult<T, TR>(arg2.Id, arg1, rea.Response);
            }
        }

        private async Task<VerifiedContext> VerifyContext(string hash, Metadata metadata)
        {
            var context = new VerifiedContext
            {
                VerifyUrl = metadata.VerifyUrl ?? "",
                Validation = Validation.Unknown,
                Origin = metadata.Url ?? ""
            };

            try
            {
                var origin = await Ref.Verify.Resolve(hash);
                if (!string.IsNullOrWhiteSpace(origin))
                {
                    context.Origin = origin;
                    context.Validation = origin == metadata.Url ? Validation.Valid : Validation.Invalid;
                }
            }
            catch (Exception e)
            {
                ReownLogger.LogError(e);
            }

            return context;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                var context = Ref.Context;
                foreach (var action in DisposeActions)
                {
                    action();
                }

                DisposeActions.Clear();

                if (Instances.ContainsKey(context))
                    Instances.Remove(context);

                Teardown();
            }

            Disposed = true;
        }
    }
}