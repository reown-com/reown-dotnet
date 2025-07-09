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

        public event EventHandler<Session> SessionExpired;
        public event EventHandler<SessionProposalEvent> SessionProposed;
        public event EventHandler<Session> SessionConnected;
        public event EventHandler<Exception> SessionConnectionErrored;
        public event EventHandler<SessionUpdateEvent> SessionUpdated;
        public event EventHandler<SessionEvent> SessionExtended;
        public event EventHandler<SessionEvent> SessionPinged;
        public event EventHandler<SessionEvent> SessionDeleted;

        public IDictionary<string, Session> ActiveSessions
        {
            get => Engine.ActiveSessions;
        }

        public IDictionary<long, ProposalStruct> PendingSessionProposals
        {
            get => Engine.PendingSessionProposals;
        }

        public PendingRequestStruct[] PendingSessionRequests
        {
            get => Engine.PendingSessionRequests;
        }

        public IWalletKitEngine Engine { get; }
        public ICoreClient CoreClient { get; }
        public Metadata Metadata { get; }

        public static async Task<IWalletKit> Init(ICoreClient coreClient, Metadata metadata, string name = null)
        {
            var wallet = new WalletKitClient(coreClient, metadata, name);
            await wallet.Initialize();

            return wallet;
        }

        private WalletKitClient(ICoreClient coreClient, Metadata metadata, string name = null)
        {
            Metadata = metadata;
            if (string.IsNullOrWhiteSpace(Metadata.Name))
                Metadata.Name = name;

            Name = string.IsNullOrWhiteSpace(name) ? "Web3Wallet" : name;
            Context = $"{Name}-context";
            CoreClient = coreClient;

            Engine = new WalletKitEngine(this);

            WrapEngineEvents();
        }

        private void WrapEngineEvents()
        {
            Engine.SessionExpired += (sender, @struct) => SessionExpired?.Invoke(sender, @struct);
            Engine.SessionProposed += (sender, @event) => SessionProposed?.Invoke(sender, @event);
            Engine.SessionConnected += (sender, @struct) => SessionConnected?.Invoke(sender, @struct);
            Engine.SessionConnectionErrored +=
                (sender, exception) => SessionConnectionErrored?.Invoke(sender, exception);
            Engine.SessionUpdated += (sender, @event) => SessionUpdated?.Invoke(sender, @event);
            Engine.SessionExtended += (sender, @event) => SessionExtended?.Invoke(sender, @event);
            Engine.SessionPinged += (sender, @event) => SessionPinged?.Invoke(sender, @event);
            Engine.SessionDeleted += (sender, @event) => SessionDeleted?.Invoke(sender, @event);
        }

        public Task Pair(string uri, bool activatePairing = false)
        {
            return Engine.Pair(uri, activatePairing);
        }

        public Task<Session> ApproveSession(long id, Namespaces namespaces, string relayProtocol = null)
        {
            return Engine.ApproveSession(id, namespaces, relayProtocol);
        }

        public Task<Session> ApproveSession(ProposalStruct proposal, params string[] approvedAddresses)
        {
            return Engine.ApproveSession(proposal, approvedAddresses);
        }

        public Task RejectSession(long id, Error reason)
        {
            return Engine.RejectSession(id, reason);
        }

        public Task RejectSession(ProposalStruct proposal, Error reason)
        {
            return Engine.RejectSession(proposal, reason);
        }

        public Task RejectSession(ProposalStruct proposal, string reason)
        {
            return Engine.RejectSession(proposal, reason);
        }

        public Task UpdateSession(string topic, Namespaces namespaces)
        {
            return Engine.UpdateSession(topic, namespaces);
        }

        public Task ExtendSession(string topic)
        {
            return Engine.ExtendSession(topic);
        }

        public Task RespondSessionRequest<T, TR>(string topic, JsonRpcResponse<TR> response)
        {
            return Engine.RespondSessionRequest<T, TR>(topic, response);
        }

        public Task EmitSessionEvent<T>(string topic, EventData<T> eventData, string chainId)
        {
            return Engine.EmitSessionEvent(topic, eventData, chainId);
        }

        public Task DisconnectSession(string topic, Error reason)
        {
            return Engine.DisconnectSession(topic, reason);
        }

        private Task Initialize()
        {
            return Engine.Init();
        }

        public void Dispose()
        {
            CoreClient?.Dispose();
        }
    }
}