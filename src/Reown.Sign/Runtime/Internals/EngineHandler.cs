using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Expirer;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;

namespace Reown.Sign
{
    public partial class Engine
    {
        async Task IEnginePrivate.OnSessionProposeRequest(string topic, JsonRpcRequest<SessionPropose> payload)
        {
            var @params = payload.Params;
            var id = payload.Id;
            try
            {
                var expiry = Clock.CalculateExpiry(Clock.FIVE_MINUTES);
                var proposal = new ProposalStruct
                {
                    Id = id,
                    PairingTopic = topic,
                    Expiry = expiry,
                    Proposer = @params.Proposer,
                    Relays = @params.Relays,
                    RequiredNamespaces = @params.RequiredNamespaces,
                    OptionalNamespaces = @params.OptionalNamespaces,
                    SessionProperties = @params.SessionProperties
                };
                await PrivateThis.SetProposal(id, proposal);
                var hash = HashUtils.HashMessage(JsonConvert.SerializeObject(payload));
                var verifyContext = await VerifyContext(hash, proposal.Proposer.Metadata);
                SessionProposed?.Invoke(this, new SessionProposalEvent
                {
                    Id = id,
                    Proposal = proposal,
                    VerifiedContext = verifyContext
                });
            }
            catch (ReownNetworkException e)
            {
                await MessageHandler.SendError<SessionPropose, SessionProposeResponseAutoReject>(id, topic,
                    Error.FromException(e));
            }
        }

        async Task IEnginePrivate.OnSessionProposeResponse(string topic, JsonRpcResponse<SessionProposeResponse> payload)
        {
            var id = payload.Id;
            logger.Log($"Got session propose response with id {id}");
            if (payload.IsError)
            {
                await Client.Proposal.Delete(id, Error.FromErrorType(ErrorType.USER_DISCONNECTED));
                SessionConnectionErrored?.Invoke(this, payload.Error.ToException());
            }
            else
            {
                var result = payload.Result;
                var proposal = Client.Proposal.Get(id);
                var selfPublicKey = proposal.Proposer.PublicKey;
                var peerPublicKey = result.ResponderPublicKey;

                var sessionTopic = await Client.CoreClient.Crypto.GenerateSharedKey(
                    selfPublicKey,
                    peerPublicKey
                );

                proposal.SessionTopic = sessionTopic;
                await Client.Proposal.Set(id, proposal);
                await Client.CoreClient.Pairing.Activate(topic);
                logger.Log($"Pairing activated for topic {topic}");

                var attempts = 5;
                do
                {
                    try
                    {
                        _ = await Client.CoreClient.Relayer.Subscribe(sessionTopic);
                        return;
                    }
                    catch (Exception e)
                    {
                        ReownLogger.LogError($"Got error subscribing to topic, attempts left: {attempts}");
                        ReownLogger.LogError(e);
                        attempts--;
                        await Task.Yield();
                    }
                } while (attempts > 0);

                throw new IOException($"Could not subscribe to session topic {sessionTopic}");
            }
        }

        async Task IEnginePrivate.OnSessionSettleRequest(string topic, JsonRpcRequest<SessionSettle> payload)
        {
            var id = payload.Id;
            var @params = payload.Params;
            logger.Log($"got session settle request with {id}");
            try
            {
                await PrivateThis.IsValidSessionSettleRequest(@params);

                var proposal = Array.Find(Client.Proposal.Values, p => p.SessionTopic == topic);

                var pairingTopic = proposal.PairingTopic;
                var relay = @params.Relay;
                var controller = @params.Controller;
                var expiry = @params.Expiry;
                var namespaces = @params.Namespaces;

                var session = new SessionStruct
                {
                    Topic = topic,
                    PairingTopic = pairingTopic,
                    Relay = relay,
                    Expiry = expiry,
                    Namespaces = namespaces,
                    Acknowledged = true,
                    Controller = controller.PublicKey,
                    Self = new Participant
                    {
                        Metadata = Client.Metadata,
                        PublicKey = ""
                    },
                    Peer = new Participant
                    {
                        PublicKey = controller.PublicKey,
                        Metadata = controller.Metadata
                    },
#pragma warning disable S6602
                    RequiredNamespaces = proposal.RequiredNamespaces
#pragma warning restore S6602
                };
                await MessageHandler.SendResult<SessionSettle, bool>(payload.Id, topic, true);
                SessionConnected?.Invoke(this, session);
            }
            catch (ReownNetworkException e)
            {
                logger.LogError("got error while performing session settle");
                logger.LogError(e);
                await MessageHandler.SendError<SessionSettle, bool>(id, topic, Error.FromException(e));
            }
        }

        async Task IEnginePrivate.OnSessionSettleResponse(string topic, JsonRpcResponse<bool> payload)
        {
            var id = payload.Id;
            var session = Client.Session.Get(topic);
            if (payload.IsError)
            {
                var error = Error.FromErrorType(ErrorType.USER_DISCONNECTED);
                await Client.Session.Delete(topic, error);
                SessionRejected?.Invoke(this, session);

                // Still used do not remove
                _sessionEventsHandlerMap[$"session_approve{id}"](this, payload);
            }
            else
            {
                await Client.Session.Update(topic, new SessionStruct
                {
                    Acknowledged = true
                });
                SessionApproved?.Invoke(this, session);
                _sessionEventsHandlerMap[$"session_approve{id}"](this, payload);
            }
        }

        async Task IEnginePrivate.OnSessionUpdateRequest(string topic, JsonRpcRequest<SessionUpdate> payload)
        {
            var @params = payload.Params;
            var id = payload.Id;
            try
            {
                await PrivateThis.IsValidUpdate(topic, @params.Namespaces);

                await Client.Session.Update(topic, new SessionStruct
                {
                    Namespaces = @params.Namespaces
                });

                await MessageHandler.SendResult<SessionUpdate, bool>(id, topic, true);
                SessionUpdateRequest?.Invoke(this, new SessionUpdateEvent
                {
                    Id = id,
                    Topic = topic,
                    Params = @params
                });
            }
            catch (ReownNetworkException e)
            {
                await MessageHandler.SendError<SessionUpdate, bool>(id, topic, Error.FromException(e));
            }
        }

        async Task IEnginePrivate.OnSessionUpdateResponse(string topic, JsonRpcResponse<bool> payload)
        {
            var id = payload.Id;
            SessionUpdated?.Invoke(this, new SessionEvent
            {
                Id = id,
                Topic = topic
            });
            // Still used, do not remove
            _sessionEventsHandlerMap[$"session_update{id}"](this, payload);
        }

        async Task IEnginePrivate.OnSessionExtendRequest(string topic, JsonRpcRequest<SessionExtend> payload)
        {
            var id = payload.Id;
            try
            {
                await PrivateThis.IsValidExtend(topic);
                await PrivateThis.SetExpiry(topic, Clock.CalculateExpiry(SessionExpiry));
                await MessageHandler.SendResult<SessionExtend, bool>(id, topic, true);
                SessionExtendRequest?.Invoke(this, new SessionEvent
                {
                    Id = id,
                    Topic = topic
                });
            }
            catch (ReownNetworkException e)
            {
                await MessageHandler.SendError<SessionExtend, bool>(id, topic, Error.FromException(e));
            }
        }

        async Task IEnginePrivate.OnSessionExtendResponse(string topic, JsonRpcResponse<bool> payload)
        {
            var id = payload.Id;
            SessionExtended?.Invoke(this, new SessionEvent
            {
                Topic = topic,
                Id = id
            });
            // Still used, do not remove
            _sessionEventsHandlerMap[$"session_extend{id}"](this, payload);
        }

        async Task IEnginePrivate.OnSessionPingRequest(string topic, JsonRpcRequest<SessionPing> payload)
        {
            var id = payload.Id;
            try
            {
                await PrivateThis.IsValidPing(topic);
                await MessageHandler.SendResult<SessionPing, bool>(id, topic, true);
                SessionPinged?.Invoke(this, new SessionEvent
                {
                    Id = id,
                    Topic = topic
                });
            }
            catch (ReownNetworkException e)
            {
                await MessageHandler.SendError<SessionPing, bool>(id, topic, Error.FromException(e));
            }
        }

        async Task IEnginePrivate.OnSessionPingResponse(string topic, JsonRpcResponse<bool> payload)
        {
            var id = payload.Id;

            // put at the end of the stack to avoid a race condition
            // where session_ping listener is not yet initialized
            await Task.Delay(500);

            SessionPinged?.Invoke(this, new SessionEvent
            {
                Id = id,
                Topic = topic
            });

            // Still used, do not remove
            _sessionEventsHandlerMap[$"session_ping{id}"](this, payload);
        }

        async Task IEnginePrivate.OnSessionDeleteRequest(string topic, JsonRpcRequest<SessionDelete> payload)
        {
            var id = payload.Id;
            try
            {
                await PrivateThis.IsValidDisconnect(topic, payload.Params);

                await MessageHandler.SendResult<SessionDelete, bool>(id, topic, true);
                await PrivateThis.DeleteSession(topic);
                SessionDeleted?.Invoke(this, new SessionEvent
                {
                    Topic = topic,
                    Id = id
                });
            }
            catch (ReownNetworkException e)
            {
                await MessageHandler.SendError<SessionDelete, bool>(id, topic, Error.FromException(e));
            }
        }

        async Task IEnginePrivate.OnSessionEventRequest(string topic, JsonRpcRequest<SessionEvent<JToken>> payload)
        {
            var @params = payload.Params;
            var id = payload.Id;
            try
            {
                var eventData = @params.Event;
                var eventName = eventData.Name;

                await IsValidSessionTopic(topic);

                _customSessionEventsHandlerMap[eventName]?.Invoke(this, @params);

                await MessageHandler.SendResult<SessionEvent<EventData<JToken>>, bool>(id, topic, true);
            }
            catch (ReownNetworkException e)
            {
                await MessageHandler.SendError<SessionEvent<JToken>, bool>(id, topic, Error.FromException(e));
            }
        }

        private async void ExpiredCallback(object sender, ExpirerEventArgs e)
        {
            var target = new ExpirerTarget(e.Target);

            if (target.Id != null && Client.PendingRequests.Keys.Contains((long)target.Id))
            {
                await PrivateThis.DeletePendingSessionRequest((long)target.Id,
                    Error.FromErrorType(ErrorType.SESSION_REQUEST_EXPIRED), true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(target.Topic))
            {
                var topic = target.Topic;
                if (!Client.Session.Keys.Contains(topic))
                {
                    return;
                }

                var session = Client.Session.Get(topic);
                await PrivateThis.DeleteSession(topic);
                SessionExpired?.Invoke(this, session);
                SessionDeleted?.Invoke(this, new SessionEvent
                {
                    Topic = topic
                });
            }
            else if (target.Id != null)
            {
                await PrivateThis.DeleteProposal((long)target.Id);
            }
        }
    }
}