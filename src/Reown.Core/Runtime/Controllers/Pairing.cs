using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Reown.Core.Common.Events;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Model.Relay;
using Reown.Core.Common.Utils;
using Reown.Core.Interfaces;
using Reown.Core.Models;
using Reown.Core.Models.Expirer;
using Reown.Core.Models.Pairing;
using Reown.Core.Models.Pairing.Methods;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     A module that handles pairing two peers and storing related data
    /// </summary>
    public class Pairing : IPairing
    {
        private const int KeyLength = 32;
        private readonly HashSet<string> _registeredMethods = new();
        private readonly Dictionary<string, string[]> _expectedPairingMethos = new();

        private readonly EventHandlerMap<JsonRpcResponse<bool>> _pairingPingResponseEvents = new();
        private bool _initialized;
        protected bool Disposed;
        private DisposeHandlerToken _pairingDeleteMessageHandler;
        private DisposeHandlerToken _pairingPingMessageHandler;

        /// <summary>
        ///     Create a new instance of the Pairing module using the given <see cref="ICoreClient" /> module
        /// </summary>
        /// <param name="coreClient">The <see cref="ICoreClient" /> module that is using this new Pairing module</param>
        public Pairing(ICoreClient coreClient)
        {
            CoreClient = coreClient;
            Store = new PairingStore(coreClient);
        }

        /// <summary>
        ///     The name for this module instance
        /// </summary>
        public string Name
        {
            get => $"{CoreClient.Context}-pairing";
        }

        /// <summary>
        ///     The context string for this Pairing module
        /// </summary>
        public string Context
        {
            get => Name;
        }

        /// <summary>
        ///     The <see cref="ICoreClient" /> module using this module instance
        /// </summary>
        public ICoreClient CoreClient { get; }

        /// <summary>
        ///     Get the <see cref="IStore{TKey,TValue}" /> module that is handling the storage of
        ///     <see cref="PairingStruct" />
        /// </summary>
        public IPairingStore Store { get; }

        /// <summary>
        ///     Get all active and inactive pairings
        /// </summary>
        public PairingStruct[] Pairings
        {
            get => Store.Values;
        }
        
        public event EventHandler<PairingCreatedEventArgs> PairingCreated;

        public event EventHandler<PairingEvent> PairingExpired;
        public event EventHandler<PairingEvent> PairingPinged;
        public event EventHandler<PairingEvent> PairingDeleted;

        /// <summary>
        ///     Initialize this pairing module. This will restore all active / inactive pairings
        ///     from storage
        /// </summary>
        public async Task Init()
        {
            if (!_initialized)
            {
                await Store.Init();
                await Cleanup();
                await RegisterTypedMessages();
                RegisterEvents();
                _initialized = true;
            }
        }
        
        /// <summary>
        ///     Parse a session proposal URI and return all information in the URI in a
        ///     new <see cref="UriParameters" /> object
        /// </summary>
        /// <param name="uri">The uri to parse</param>
        /// <returns>
        ///     A new <see cref="UriParameters" /> object that contains all data
        ///     parsed from the given uri
        /// </returns>
        public UriParameters ParseUri(string uri)
        {
            var pathStart = uri.IndexOf(':');
            int? pathEnd = uri.Contains('?') ? uri.IndexOf('?') : null;
            var protocol = uri[..pathStart];

            var path = pathEnd != null ? uri.Substring(pathStart + 1, (int)pathEnd - (pathStart + 1)) : uri[(pathStart + 1)..];

            var requiredValues = path.Split("@");
            var queryString = pathEnd != null ? uri[(int)pathEnd..] : "";
            var queryParams = UrlUtils.ParseQs(queryString);

            var methodsParam = queryParams.GetValueOrDefault("methods");
            var methods = !string.IsNullOrEmpty(methodsParam)
                ? methodsParam.Split(",")
                : null;

            var result = new UriParameters
            {
                Protocol = protocol,
                Topic = requiredValues[0],
                Version = int.Parse(requiredValues[1]),
                SymKey = queryParams["symKey"],
                Relay = new ProtocolOptions
                {
                    Protocol = queryParams["relay-protocol"],
                    Data = queryParams.GetValueOrDefault("relay-data")
                },
                Methods = methods
            };

            return result;
        }

        /// <summary>
        ///     Pair with a peer using the given uri. The uri must be in the correct
        ///     format otherwise an exception will be thrown. You may (optionally) pair
        ///     without activating the pairing. By default the pairing will be activated before
        ///     it is returned
        /// </summary>
        /// <param name="uri">The URI to pair with</param>
        /// <returns>The pairing data that can be used to pair with the peer</returns>
        public async Task<PairingStruct> Pair(string uri, bool activatePairing = true)
        {
            IsInitialized();
            ValidateUri(uri);

            var uriParams = ParseUri(uri);

            var topic = uriParams.Topic;
            var symKey = uriParams.SymKey;
            var relay = uriParams.Relay;
            var methods = uriParams.Methods;

            if (Store.Keys.Contains(topic))
            {
                throw new ArgumentException($"Topic {topic} already has pairing");
            }

            if (await CoreClient.Crypto.HasKeys(topic))
            {
                throw new ArgumentException($"Topic {topic} already has keychain");
            }

            var expiry = Clock.CalculateExpiry(Clock.FIVE_MINUTES);
            var pairing = new PairingStruct
            {
                Topic = topic,
                Relay = relay,
                Expiry = expiry,
                Active = false,
                Methods = methods
            };

            await Store.Set(topic, pairing);
            await CoreClient.Crypto.SetSymKey(symKey, topic);

            CoreClient.Expirer.Set(topic, expiry);

            if (activatePairing)
            {
                await ActivatePairing(topic);
            }
            
            PairingCreated?.Invoke(this, new PairingCreatedEventArgs
            {
                Pairing = pairing
            });
            
            await CoreClient.Relayer.Subscribe(topic, new SubscribeOptions
            {
                Relay = relay
            });

            return pairing;
        }

        /// <summary>
        ///     Create a new pairing at the given pairing topic
        /// </summary>
        /// <returns>
        ///     A new instance of <see cref="CreatePairingData" /> that includes the pairing topic and
        ///     uri
        /// </returns>
        public async Task<CreatePairingData> Create(string[] methods = null)
        {
            var symKeyRaw = new byte[KeyLength];
            RandomNumberGenerator.Fill(symKeyRaw);
            var symKey = symKeyRaw.ToHex();
            var topic = await CoreClient.Crypto.SetSymKey(symKey);
            var expiry = Clock.CalculateExpiry(Clock.FIVE_MINUTES);
            var relay = new ProtocolOptions
            {
                Protocol = RelayProtocols.Default
            };
            var pairing = new PairingStruct
            {
                Topic = topic,
                Expiry = expiry,
                Relay = relay,
                Active = false
            };
            
            var uri = $"{ICoreClient.Protocol}:{topic}@{ICoreClient.Version}?"
                .AddQueryParam("symKey", symKey)
                .AddQueryParam("relay-protocol", relay.Protocol)
                .AddQueryParam("expiryTimestamp", expiry.ToString());

            if (!string.IsNullOrWhiteSpace(relay.Data))
                uri = uri.AddQueryParam("relay-data", relay.Data);

            if (methods is { Length: > 0 })
                uri = uri.AddQueryParam("methods", string.Join(",", methods));

            await Store.Set(topic, pairing);
            await CoreClient.Relayer.Subscribe(topic);
            
            PairingCreated?.Invoke(this, new PairingCreatedEventArgs
            {
                Pairing = pairing
            });
            
            CoreClient.Expirer.Set(topic, expiry);

            return new CreatePairingData
            {
                Topic = topic,
                Uri = uri
            };
        }

        /// <summary>
        ///     Activate a previously created pairing at the given topic
        /// </summary>
        /// <param name="topic">The topic of the pairing to activate</param>
        public Task Activate(string topic)
        {
            return ActivatePairing(topic);
        }

        /// <summary>
        ///     Subscribe to method requests
        /// </summary>
        /// <param name="methods">The methods to register and subscribe</param>
        public Task Register(string[] methods)
        {
            IsInitialized();
            foreach (var method in methods)
            {
                _registeredMethods.Add(method);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Update the expiration of an existing pairing at the given topic
        /// </summary>
        /// <param name="topic">The topic of the pairing to update</param>
        /// <param name="expiration">The new expiration date as a unix timestamp (seconds)</param>
        /// <returns></returns>
        public Task UpdateExpiry(string topic, long expiration)
        {
            IsInitialized();
            return Store.Update(topic, new PairingStruct
            {
                Expiry = expiration
            });
        }

        /// <summary>
        ///     Update the metadata of an existing pairing at the given topic
        /// </summary>
        /// <param name="topic">The topic of the pairing to update</param>
        /// <param name="metadata">The new metadata</param>
        public Task UpdateMetadata(string topic, Metadata metadata)
        {
            IsInitialized();
            return Store.Update(topic, new PairingStruct
            {
                PeerMetadata = metadata
            });
        }

        /// <summary>
        ///     Ping an existing pairing at the given topic
        /// </summary>
        /// <param name="topic">The topic of the pairing to ping</param>
        public async Task Ping(string topic)
        {
            IsInitialized();
            await IsValidPairingTopic(topic);
            if (Store.Keys.Contains(topic))
            {
                var id = await CoreClient.MessageHandler.SendRequest<PairingPing, bool>(topic, new PairingPing());
                var done = new TaskCompletionSource<bool>();

                _pairingPingResponseEvents.ListenOnce($"pairing_ping{id}", (sender, args) =>
                {
                    if (args.IsError)
                        done.SetException(args.Error.ToException());
                    else
                        done.SetResult(args.Result);
                });

                await done.Task;
            }
        }

        /// <summary>
        ///     Disconnect an existing pairing at the given topic
        /// </summary>
        /// <param name="topic">The topic of the pairing to disconnect</param>
        public async Task Disconnect(string topic)
        {
            IsInitialized();
            await IsValidPairingTopic(topic);

            if (Store.Keys.Contains(topic))
            {
                var error = Error.FromErrorType(ErrorType.USER_DISCONNECTED);
                await CoreClient.MessageHandler.SendRequest<PairingDelete, bool>(topic,
                    new PairingDelete
                    {
                        Code = error.Code,
                        Message = error.Message
                    });
                await DeletePairing(topic);
            }
        }

        private void RegisterEvents()
        {
            PairingCreated += PairingCreatedCallback;
            CoreClient.Expirer.Expired += ExpiredCallback;
        }
        
        private void PairingCreatedCallback(object sender, PairingCreatedEventArgs e)
        {
            if (e.Pairing.Methods is { Length: > 0 })
                _expectedPairingMethos[e.Pairing.Topic] = e.Pairing.Methods;
        }

        private async Task RegisterTypedMessages()
        {
            _pairingDeleteMessageHandler = await CoreClient.MessageHandler.HandleMessageType<PairingDelete, bool>(OnPairingDeleteRequest, null);
            _pairingPingMessageHandler = await CoreClient.MessageHandler.HandleMessageType<PairingPing, bool>(OnPairingPingRequest, OnPairingPingResponse);
        }

        public bool TryGetExpectedMethods(string topic, out string[] methods)
        {
            return _expectedPairingMethos.TryGetValue(topic, out methods);
        }

        private async Task ActivatePairing(string topic)
        {
            var expiry = Clock.CalculateExpiry(Clock.THIRTY_DAYS);
            await Store.Update(topic, new PairingStruct
            {
                Active = true,
                Expiry = expiry
            });

            CoreClient.Expirer.Set(topic, expiry);
        }

        private async Task DeletePairing(string topic)
        {
            var expirerHasDeleted = !CoreClient.Expirer.Has(topic);
            var pairingHasDeleted = !Store.Keys.Contains(topic);
            var symKeyHasDeleted = !await CoreClient.Crypto.HasKeys(topic);
            
            _ = _expectedPairingMethos.Remove(topic);

            await CoreClient.Relayer.Unsubscribe(topic);
            await Task.WhenAll(
                pairingHasDeleted
                    ? Task.CompletedTask
                    : Store.Delete(topic, Error.FromErrorType(ErrorType.USER_DISCONNECTED)),
                symKeyHasDeleted ? Task.CompletedTask : CoreClient.Crypto.DeleteSymKey(topic),
                expirerHasDeleted ? Task.CompletedTask : CoreClient.Expirer.Delete(topic)
            );
        }

        private Task Cleanup()
        {
            var pairingTopics = (from pair in Store.Values.Where(e => e.Expiry != null)
                where pair.Expiry != null && Clock.IsExpired(pair.Expiry.Value)
                select pair.Topic).ToList();

            return Task.WhenAll(
                pairingTopics.Select(DeletePairing)
            );
        }

        private async Task IsValidPairingTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new ArgumentNullException(nameof(topic));
            }

            if (!Store.Keys.Contains(topic))
                throw new KeyNotFoundException($"Pairing topic {topic} not found.");

            var expiry = Store.Get(topic).Expiry;
            if (expiry != null && Clock.IsExpired(expiry.Value))
            {
                await DeletePairing(topic);
                throw new ExpiredException($"Pairing topic {topic} has expired.");
            }
        }

        private static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                new Uri(url);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ValidateUri(string uri)
        {
            if (!IsValidUrl(uri))
                throw new FormatException($"Invalid URI format: {uri}");
        }

        private void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(Pairing)} module not initialized.");
            }
        }

        private async Task OnPairingPingRequest(string topic, JsonRpcRequest<PairingPing> payload)
        {
            var id = payload.Id;
            try
            {
                await IsValidPairingTopic(topic);

                await CoreClient.MessageHandler.SendResult<PairingPing, bool>(id, topic, true);
                PairingPinged?.Invoke(this, new PairingEvent
                {
                    Topic = topic,
                    Id = id
                });
            }
            catch (ReownNetworkException e)
            {
                await CoreClient.MessageHandler.SendError<PairingPing, bool>(id, topic, Error.FromException(e));
            }
        }

        private async Task OnPairingPingResponse(string topic, JsonRpcResponse<bool> payload)
        {
            var id = payload.Id;

            // put at the end of the stack to avoid a race condition
            // where session_ping listener is not yet initialized
            await Task.Delay(500);

            PairingPinged?.Invoke(this, new PairingEvent
            {
                Id = id,
                Topic = topic
            });

            _pairingPingResponseEvents[$"pairing_ping{id}"](this, payload);
        }

        private async Task OnPairingDeleteRequest(string topic, JsonRpcRequest<PairingDelete> payload)
        {
            var id = payload.Id;
            try
            {
                await IsValidDisconnect(topic, payload.Params);

                await CoreClient.MessageHandler.SendResult<PairingDelete, bool>(id, topic, true);
                await DeletePairing(topic);
                PairingDeleted?.Invoke(this, new PairingEvent
                {
                    Topic = topic,
                    Id = id
                });
            }
            catch (ReownNetworkException e)
            {
                await CoreClient.MessageHandler.SendError<PairingDelete, bool>(id, topic, Error.FromException(e));
            }
        }

        private async Task IsValidDisconnect(string topic, Error reason)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new ArgumentNullException(nameof(topic));
            }

            await IsValidPairingTopic(topic);
        }

        private async void ExpiredCallback(object sender, ExpirerEventArgs e)
        {
            ReownLogger.Log($"Expired topic {e.Target}");
            var target = new ExpirerTarget(e.Target);

            if (string.IsNullOrWhiteSpace(target.Topic)) return;

            var topic = target.Topic;
            if (Store.Keys.Contains(topic))
            {
                await DeletePairing(topic);
                PairingExpired?.Invoke(this, new PairingEvent
                {
                    Topic = topic
                });
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                Store?.Dispose();
                PairingCreated -= PairingCreatedCallback;
                _pairingDeleteMessageHandler.Dispose();
                _pairingPingMessageHandler.Dispose();
            }

            Disposed = true;
        }
    }
}