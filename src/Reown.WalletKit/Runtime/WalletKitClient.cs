using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.Core;
using Reown.Core.Interfaces;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine.Events;
using Reown.WalletKit.Controllers;
using Reown.WalletKit.Interfaces;

namespace Reown.WalletKit
{
    public class WalletKitClient : IWalletKit
    {
        public string Name { get; }
        public string Context { get; }

        public event EventHandler<SessionStruct> SessionExpired;
        public event EventHandler<SessionProposalEvent> SessionProposed;
        public event EventHandler<SessionStruct> SessionConnected;
        public event EventHandler<Exception> SessionConnectionErrored;
        public event EventHandler<SessionUpdateEvent> SessionUpdated;
        public event EventHandler<SessionEvent> SessionExtended;
        public event EventHandler<SessionEvent> SessionPinged;
        public event EventHandler<SessionEvent> SessionDeleted;

        public IDictionary<string, SessionStruct> ActiveSessions
        {
            get
            {
                return this.Engine.ActiveSessions;
            }
        }

        public IDictionary<long, ProposalStruct> PendingSessionProposals
        {
            get
            {
                return this.Engine.PendingSessionProposals;
            }
        }

        public PendingRequestStruct[] PendingSessionRequests
        {
            get
            {
                return this.Engine.PendingSessionRequests;
            }
        }

        public IWalletKitEngine Engine { get; }
        public ICoreClient CoreClient { get; }
        public Metadata Metadata { get; }
    
        public static async Task<WalletKitClient> Init(ICoreClient coreClient, Metadata metadata, string name = null)
        {
            var wallet = new WalletKitClient(coreClient, metadata, name);
            await wallet.Initialize();

            return wallet;
        }
    
        private WalletKitClient(ICoreClient coreClient, Metadata metadata, string name = null)
        {
            this.Metadata = metadata;
            if (string.IsNullOrWhiteSpace(this.Metadata.Name))
                this.Metadata.Name = name;
        
            this.Name = string.IsNullOrWhiteSpace(name) ? "Web3Wallet" : name;
            this.Context = $"{Name}-context";
            this.CoreClient = coreClient;
        
            this.Engine = new WalletKitEngine(this);
        
            WrapEngineEvents();
        }

        private void WrapEngineEvents()
        {
            Engine.SessionExpired += (sender, @struct) => this.SessionExpired?.Invoke(sender, @struct);
            Engine.SessionProposed += (sender, @event) => this.SessionProposed?.Invoke(sender, @event);
            Engine.SessionConnected += (sender, @struct) => this.SessionConnected?.Invoke(sender, @struct);
            Engine.SessionConnectionErrored +=
                (sender, exception) => this.SessionConnectionErrored?.Invoke(sender, exception);
            Engine.SessionUpdated += (sender, @event) => this.SessionUpdated?.Invoke(sender, @event);
            Engine.SessionExtended += (sender, @event) => this.SessionExtended?.Invoke(sender, @event);
            Engine.SessionPinged += (sender, @event) => this.SessionPinged?.Invoke(sender, @event);
            Engine.SessionDeleted += (sender, @event) => this.SessionDeleted?.Invoke(sender, @event);
        }
    
        public Task Pair(string uri, bool activatePairing = false)
        {
            return this.Engine.Pair(uri, activatePairing);
        }

        public Task<SessionStruct> ApproveSession(long id, Namespaces namespaces, string relayProtocol = null)
        {
            return this.Engine.ApproveSession(id, namespaces, relayProtocol);
        }

        public Task<SessionStruct> ApproveSession(ProposalStruct proposal, params string[] approvedAddresses)
        {
            return this.Engine.ApproveSession(proposal, approvedAddresses);
        }

        public Task RejectSession(long id, Error reason)
        {
            return this.Engine.RejectSession(id, reason);
        }

        public Task RejectSession(ProposalStruct proposal, Error reason)
        {
            return this.Engine.RejectSession(proposal, reason);
        }

        public Task RejectSession(ProposalStruct proposal, string reason)
        {
            return this.Engine.RejectSession(proposal, reason);
        }

        public Task UpdateSession(string topic, Namespaces namespaces)
        {
            return this.Engine.UpdateSession(topic, namespaces);
        }

        public Task ExtendSession(string topic)
        {
            return this.Engine.ExtendSession(topic);
        }

        public Task RespondSessionRequest<T, TR>(string topic, JsonRpcResponse<TR> response)
        {
            return this.Engine.RespondSessionRequest<T, TR>(topic, response);
        }

        public Task EmitSessionEvent<T>(string topic, EventData<T> eventData, string chainId)
        {
            return this.Engine.EmitSessionEvent(topic, eventData, chainId);
        }

        public Task DisconnectSession(string topic, Error reason)
        {
            return this.Engine.DisconnectSession(topic, reason);
        }
        
        private Task Initialize()
        {
            return this.Engine.Init();
        }

        public void Dispose()
        {
            CoreClient?.Dispose();
        }
    }
}
