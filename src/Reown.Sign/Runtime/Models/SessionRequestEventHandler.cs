using System;
using System.Threading.Tasks;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Interfaces;
using Reown.Core.Models.MessageHandler;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.Sign.Models
{
    /// <summary>
    ///     A sub-class of <see cref="TypedEventHandler{T,TR}" /> that fixes complex nesting issue with
    ///     SessionRequest<T>. The purpose of this class is to un-nest the SessionRequest<T> object
    /// </summary>
    /// <typeparam name="T">The type of the session request</typeparam>
    /// <typeparam name="TR">The type of the response for the session request</typeparam>
    public class SessionRequestEventHandler<T, TR> : TypedEventHandler<T, TR>
    {
        private readonly IEnginePrivate _enginePrivate;

        private TypedEventHandler<SessionRequest<T>, TR> _wrappedRef;

        protected SessionRequestEventHandler(ICoreClient engine, IEnginePrivate enginePrivate) : base(engine)
        {
            _enginePrivate = enginePrivate;
        }

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
        public static TypedEventHandler<T, TR> GetInstance(ICoreClient engine, IEnginePrivate enginePrivate)
        {
            var context = engine.Context;

            if (TryGetLiveInstance(engine, out var instance))
                return instance;

            var newInstance = new SessionRequestEventHandler<T, TR>(engine, enginePrivate);

            Instances.Add(context, newInstance);

            return newInstance;
        }

        protected override TypedEventHandler<T, TR> BuildNew(ICoreClient @ref,
            Func<RequestEventArgs<T, TR>, bool> requestPredicate, Func<ResponseEventArgs<TR>, bool> responsePredicate)
        {
            var instance = new SessionRequestEventHandler<T, TR>(@ref, _enginePrivate)
            {
                RequestPredicate = requestPredicate,
                ResponsePredicate = responsePredicate
            };

            TrackDerivedInstance(instance);

            return instance;
        }

        /// <summary>
        ///     Subscribes to the shared handler for the wrapped <see cref="SessionRequest{T}" /> type pair and
        ///     completes only once that handler is itself registered, so that awaiting
        ///     <see cref="TypedEventHandler{T,TR}.WhenRegisteredAsync" /> on this instance guarantees the whole
        ///     chain is live.
        /// </summary>
        /// <returns>A task that completes once the wrapped handler is registered</returns>
        protected override async Task SetupAsync()
        {
            var wrappedRef = TypedEventHandler<SessionRequest<T>, TR>.GetInstance(Ref);

            _wrappedRef = wrappedRef;

            wrappedRef.OnRequest += WrappedRefOnOnRequest;
            wrappedRef.OnResponse += WrappedRefOnOnResponse;

            await wrappedRef.WhenRegisteredAsync();
        }

        /// <summary>
        ///     Detaches from the shared handler for the wrapped <see cref="SessionRequest{T}" /> type pair. This
        ///     runs whenever the last subscription is removed, not only on disposal, so re-subscribing this
        ///     instance cannot leave a second set of forwarding handlers behind.
        /// </summary>
        protected override void Teardown()
        {
            var wrappedRef = _wrappedRef;
            if (wrappedRef != null)
            {
                _wrappedRef = null;

                wrappedRef.OnRequest -= WrappedRefOnOnRequest;
                wrappedRef.OnResponse -= WrappedRefOnOnResponse;
            }

            base.Teardown();
        }

        /// <summary>
        ///     Disposes this handler and, when this is the instance registered for the client's context, the
        ///     shared handler for the wrapped <see cref="SessionRequest{T}" /> type pair as well. Instances
        ///     produced by the filter methods only unsubscribe, because they share that wrapped handler.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be released</param>
        protected override void Dispose(bool disposing)
        {
            var disposeWrapped = disposing && !Disposed && IsRegisteredInstance();

            base.Dispose(disposing);

            if (disposeWrapped)
            {
                TypedEventHandler<SessionRequest<T>, TR>.DisposeInstance(Ref);
            }
        }

        private bool IsRegisteredInstance()
        {
            return Instances.TryGetValue(Ref.Context, out var registered) && ReferenceEquals(registered, this);
        }

        private Task WrappedRefOnOnResponse(ResponseEventArgs<TR> e)
        {
            return base.ResponseCallback(e.Topic, e.Response);
        }

        private async Task WrappedRefOnOnRequest(RequestEventArgs<SessionRequest<T>, TR> e)
        {
            // Ensure that the request is for us
            var method = RpcMethodAttribute.MethodForType<T>();

            var sessionRequest = e.Request.Params.Request;

            if (sessionRequest.Method != method) return;

            // Set inner request id to match outer request id
            sessionRequest.Id = e.Request.Id;

            // Add to pending requests
            // We can't do a simple cast, so we need to copy all the data
            await _enginePrivate.SetPendingSessionRequest(new PendingRequestStruct
            {
                Id = e.Request.Id,
                Parameters = new SessionRequest<object>
                {
                    ChainId = e.Request.Params.ChainId,
                    Request = new JsonRpcRequest<object>
                    {
                        Id = sessionRequest.Id,
                        Method = sessionRequest.Method,
                        Params = sessionRequest.Params
                    }
                },
                Topic = e.Topic
            });

            await base.RequestCallback(e.Topic, sessionRequest);

            await _enginePrivate.DeletePendingSessionRequest(e.Request.Id, Error.FromErrorType(ErrorType.GENERIC));
        }
    }
}