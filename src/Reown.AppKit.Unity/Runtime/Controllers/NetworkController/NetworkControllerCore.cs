using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Reown.AppKit.Unity
{
    public class NetworkControllerCore : NetworkController
    {
        protected override Task InitializeAsyncCore(IEnumerable<Chain> supportedChains)
        {
            var supportedChainsArray = supportedChains.ToArray();
            Chains = new ReadOnlyDictionary<string, Chain>(supportedChainsArray.ToDictionary(c => c.ChainId, c => c));

            ActiveChain = supportedChainsArray[0];

            return Task.CompletedTask;
        }

        protected override async Task ChangeActiveChainAsyncCore(Chain chain)
        {
            // Request connector to change active chain.
            // If connector approves the change, it will trigger the ChainChanged event.
            await AppKit.ConnectorController.ChangeActiveChainAsync(chain);

            var previousChain = ActiveChain;
            ActiveChain = chain;
            OnChainChanged(new ChainChangedEventArgs(previousChain, chain));

            AppKit.EventsController.SendEvent(new Event
            {
                name = "SWITCH_NETWORK",
                properties = new Dictionary<string, object>
                {
                    {
                        "network", chain.ChainId
                    }
                }
            });
        }

        protected override void ConnectorChainChangedHandlerCore(object sender, Connector.ChainChangedEventArgs e)
        {
            if (ActiveChain?.ChainId == e.ChainId)
                return;

            var chain = Chains.GetValueOrDefault(e.ChainId);

            var previousChain = ActiveChain;
            ActiveChain = chain;
            OnChainChanged(new ChainChangedEventArgs(previousChain, chain));
        }

        protected override void ConnectorAccountConnectedHandlerCore(object sender, Connector.AccountConnectedEventArgs accountConnectedEventArgs)
        {
            var previousChain = ActiveChain;
            var accounts = accountConnectedEventArgs.Accounts.ToArray();

            var defaultAccount = accountConnectedEventArgs.Account;

            if (Chains.TryGetValue(defaultAccount.ChainId, out var defaultAccountChain))
            {
                ActiveChain = defaultAccountChain;
                OnChainChanged(new ChainChangedEventArgs(previousChain, defaultAccountChain));
                return;
            }

            var account = Array.Find(accounts, a => Chains.ContainsKey(a.ChainId));
            if (account == default)
            {
                ActiveChain = null;
                OnChainChanged(new ChainChangedEventArgs(previousChain, null));
                return;
            }

            ActiveChain = Chains[account.ChainId];
            OnChainChanged(new ChainChangedEventArgs(previousChain, ActiveChain));
        }
    }
}