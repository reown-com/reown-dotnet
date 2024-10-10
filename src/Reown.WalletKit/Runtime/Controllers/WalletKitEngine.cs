using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;
using Reown.WalletKit.Interfaces;


namespace Reown.WalletKit.Controllers
{
    public class WalletKitEngine : IWalletKitEngine
    {
        private bool _initialized;

        public event EventHandler<SessionStruct> SessionExpired;
        public event EventHandler<SessionAuthenticate> SessionAuthenticate; 
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
                return this.SignClient.Session.ToDictionary();
            }
        }

        public IDictionary<long, ProposalStruct> PendingSessionProposals
        {
            get
            {
                return this.SignClient.Proposal.ToDictionary();
            }
        }

        public PendingRequestStruct[] PendingSessionRequests
        {
            get
            {
                return this.SignClient.PendingSessionRequests;
            }
        }

        public ISignClient SignClient { get; private set; }
        public IWalletKit Client { get; }
    
        public WalletKitEngine(IWalletKit client)
        {
            Client = client;
        }
    
        public async Task Init()
        {
            SignClient = await Sign.SignClient.Init(new SignClientOptions()
            {
                CoreClient = Client.CoreClient, Metadata = Client.Metadata
            });
        
            InitializeEventListeners();

            _initialized = true;
        }

        public async Task Pair(string uri, bool activatePairing = false)
        {
            IsInitialized();
            await this.Client.CoreClient.Pairing.Pair(uri, activatePairing);
        }

        public async Task<SessionStruct> ApproveSession(long id, Namespaces namespaces, string relayProtocol = null)
        {
            var data = await this.SignClient.Approve(new ApproveParams()
            {
                Id = id, Namespaces = namespaces, RelayProtocol = relayProtocol
            });

            await data.Acknowledged();

            return this.SignClient.Session.Get(data.Topic);
        }

        public Task<SessionStruct> ApproveSession(ProposalStruct proposal, params string[] approvedAddresses)
        {
            var param = proposal.ApproveProposal(approvedAddresses);
            return ApproveSession(param.Id, param.Namespaces, param.RelayProtocol);
        }

        public Task RejectSession(long id, Error reason)
        {
            return this.SignClient.Reject(new RejectParams() { Id = id, Reason = reason });
        }

        public Task RejectSession(ProposalStruct proposal, Error reason)
        {
            var parm = proposal.RejectProposal(reason);
            return RejectSession(parm.Id, parm.Reason);
        }

        public Task RejectSession(ProposalStruct proposal, string reason)
        {
            var parm = proposal.RejectProposal(reason);
            return RejectSession(parm.Id, parm.Reason);
        }

        public async Task UpdateSession(string topic, Namespaces namespaces)
        {
            await (await this.SignClient.UpdateSession(topic, namespaces)).Acknowledged();
        }

        public async Task ExtendSession(string topic)
        {
            await (await this.SignClient.Extend(topic)).Acknowledged();
        }

        public async Task RespondSessionRequest<T, TR>(string topic, JsonRpcResponse<TR> response)
        {
            await this.SignClient.Respond<T, TR>(topic, response);
        }

        public async Task EmitSessionEvent<T>(string topic, EventData<T> eventData, string chainId)
        {
            await this.SignClient.Emit(topic, eventData, chainId);
        }

        public async Task DisconnectSession(string topic, Error reason)
        {
            await this.SignClient.Disconnect(topic, reason);
        }

        private void InitializeEventListeners()
        {
            // Propagate sign events
            SignClient.SessionAuthenticateRequest += (sender, @event) => SessionAuthenticate?.Invoke(sender, @event);
            SignClient.SessionProposed += (sender, @event) => this.SessionProposed?.Invoke(sender, @event);
            SignClient.SessionDeleted += (sender, @event) => this.SessionDeleted?.Invoke(sender, @event);
            SignClient.SessionPinged += (sender, @event) => this.SessionPinged?.Invoke(sender, @event);
            SignClient.SessionExtendRequest += (sender, @event) => this.SessionExtended?.Invoke(sender, @event);
            SignClient.SessionExpired += (sender, @struct) => this.SessionExpired?.Invoke(sender, @struct);
            SignClient.SessionConnected += (sender, @struct) => this.SessionConnected?.Invoke(sender, @struct);
            SignClient.SessionConnectionErrored +=
                (sender, exception) => this.SessionConnectionErrored?.Invoke(sender, exception);
            SignClient.SessionUpdateRequest += (sender, @event) => this.SessionUpdated?.Invoke(sender, @event);
        }

        private void IsInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{nameof(WalletKitEngine)} module not initialized.");
            }
        }
    }
}
