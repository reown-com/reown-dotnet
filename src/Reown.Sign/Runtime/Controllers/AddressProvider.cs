using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Reown.Core;
using Reown.Core.Common.Utils;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine.Events;

namespace Reown.Sign.Controllers
{
    public class AddressProvider : IAddressProvider
    {
        private ISignClient _client;
        private bool _disposed;

        private DefaultData _state;

        public AddressProvider(ISignClient client)
        {
            _client = client;
            Sessions = client.Session;

            // set the first connected session to the default one
            client.SessionConnected += ClientOnSessionConnected;
            client.SessionDeleted += ClientOnSessionDeleted;
            client.SessionUpdateRequest += ClientOnSessionUpdated;
            client.SessionApproved += ClientOnSessionConnected;
        }

        public string Name
        {
            get => $"{_client.Name}-address-provider";
        }

        public string Context
        {
            get => Name;
        }

        public event EventHandler<DefaultsLoadingEventArgs> DefaultsLoaded;

        public bool HasDefaultSession
        {
            get => !string.IsNullOrWhiteSpace(DefaultSession.Topic) && DefaultSession.Namespaces != null;
        }

        public SessionStruct DefaultSession
        {
            get => _state.Session;
            set => _state.Session = value;
        }

        public string DefaultNamespace
        {
            get => _state.Namespace;
            set => _state.Namespace = value;
        }

        public string DefaultChainId
        {
            get => _state.ChainId;
            set => _state.ChainId = value;
        }

        public ISession Sessions { get; private set; }

        public virtual async Task LoadDefaultsAsync()
        {
            var key = $"{Context}-default-session";
            if (await _client.CoreClient.Storage.HasItem(key))
            {
                var state = await _client.CoreClient.Storage.GetItem<DefaultData>(key);
                var sessionExpiry = state.Session.Expiry;

                _state = sessionExpiry != null && !Clock.IsExpired(sessionExpiry.Value)
                    ? state
                    : new DefaultData();
            }
            else
            {
                _state = new DefaultData();
            }

            DefaultsLoaded?.Invoke(this, new DefaultsLoadingEventArgs(_state));
        }

        public async Task SetDefaultNamespaceAsync(string @namespace)
        {
            if (string.IsNullOrWhiteSpace(@namespace))
            {
                throw new ArgumentNullException(nameof(@namespace));
            }

            if (!DefaultSession.Namespaces.ContainsKey(@namespace))
            {
                throw new InvalidOperationException($"Namespace {@namespace} is not available in the current session");
            }

            DefaultNamespace = @namespace;
            await SaveDefaults();
        }

        public async Task SetDefaultChainIdAsync(string chainId)
        {
            if (string.IsNullOrWhiteSpace(chainId))
            {
                throw new ArgumentNullException(nameof(chainId));
            }

            if (!Utils.IsValidChainId(chainId))
            {
                throw new ArgumentException("The format of 'chainId' is invalid. Must be in the format of 'namespace:chainId' (e.g. 'eip155:10'). See CAIP-2 for more information.");
            }

            DefaultChainId = chainId;
            await SaveDefaults();
        }

        public Caip25Address CurrentAddress(string chainId = null, SessionStruct session = default)
        {
            chainId ??= DefaultChainId;
            if (string.IsNullOrWhiteSpace(session.Topic))
            {
                session = DefaultSession;
            }

            return session.CurrentAddress(chainId);
        }

        public IEnumerable<Caip25Address> AllAddresses(string @namespace = null, SessionStruct session = default)
        {
            @namespace ??= DefaultNamespace;
            if (string.IsNullOrWhiteSpace(session.Topic)) // default
                session = DefaultSession;

            return session.AllAddresses(@namespace);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual async Task SaveDefaults()
        {
            await _client.CoreClient.Storage.SetItem($"{Context}-default-session", _state);
        }

        private async void ClientOnSessionUpdated(object sender, SessionEvent e)
        {
            if (DefaultSession.Topic == e.Topic)
            {
                DefaultSession = Sessions.Get(e.Topic);
                await UpdateDefaultChainIdAndNamespaceAsync();
            }
        }

        private async void ClientOnSessionDeleted(object sender, SessionEvent e)
        {
            if (DefaultSession.Topic == e.Topic)
            {
                DefaultSession = default;
                await UpdateDefaultChainIdAndNamespaceAsync();
            }
        }

        private async void ClientOnSessionConnected(object sender, SessionStruct e)
        {
            DefaultSession = e;
            await UpdateDefaultChainIdAndNamespaceAsync();
        }

        private async Task UpdateDefaultChainIdAndNamespaceAsync()
        {
            if (HasDefaultSession)
            {
                // Check if current default namespace is still valid with the current session
                var currentDefault = DefaultNamespace;


                if (currentDefault != null && DefaultSession.Namespaces.ContainsKey(currentDefault))
                {
                    if (!DefaultSession.Namespaces[DefaultNamespace].TryGetChains(out var approvedChains))
                    {
                        throw new InvalidOperationException("Could not get chains for current default namespace");
                    }

                    // Check if current default chain is still valid with the current session
                    var currentChain = DefaultChainId;

                    if (currentChain == null || !approvedChains.Contains(currentChain))
                    {
                        // If the current default chain is not valid, let's use the first one
                        DefaultChainId = approvedChains[0];
                    }
                }
                else
                {
                    // If DefaultNamespace is null or not found in current available spaces, update it
                    DefaultNamespace = DefaultSession.Namespaces.Keys.FirstOrDefault();
                    if (DefaultNamespace != null)
                    {
                        if (!DefaultSession.Namespaces[DefaultNamespace].TryGetChains(out var approvedChains))
                        {
                            throw new InvalidOperationException("Could not get chains for current default namespace");
                        }

                        DefaultChainId = approvedChains[0];
                    }
                    else
                    {
                        throw new InvalidOperationException("Could not figure out default chain and namespace");
                    }
                }

                await SaveDefaults();
            }
            else
            {
                DefaultNamespace = null;
                DefaultChainId = null;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _client.SessionConnected -= ClientOnSessionConnected;
                _client.SessionDeleted -= ClientOnSessionDeleted;
                _client.SessionUpdateRequest -= ClientOnSessionUpdated;
                _client.SessionApproved -= ClientOnSessionConnected;

                _client = null;
                Sessions = null;
                DefaultNamespace = null;
                DefaultSession = default;
            }

            _disposed = true;
        }

        public struct DefaultData
        {
            public SessionStruct Session;
            public string Namespace;
            public string ChainId;
        }
    }
}