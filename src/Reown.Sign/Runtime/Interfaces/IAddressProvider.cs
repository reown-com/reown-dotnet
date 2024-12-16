using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.Core.Common;
using Reown.Sign.Models;

namespace Reown.Sign.Interfaces
{
    public interface IAddressProvider : IModule
    {
        bool HasDefaultSession { get; }

        Session DefaultSession { get; set; }

        string DefaultNamespace { get; }

        string DefaultChainId { get; }

        ISession Sessions { get; }
        event EventHandler<DefaultsLoadingEventArgs> DefaultsLoaded;

        Task SetDefaultNamespaceAsync(string @namespace);

        Task SetDefaultChainIdAsync(string chainId);

        public Account CurrentAccount(string chainId = null, Session session = null);

        [Obsolete("Use CurrentAccount instead.")]
        Account CurrentAddress(string chainId = null, Session session = null);

        public IEnumerable<Account> AllAccounts(string @namespace = null, Session session = null);

        [Obsolete("Use AllAccounts instead.")]
        IEnumerable<Account> AllAddresses(string @namespace = null, Session session = null);

        public Task LoadDefaultsAsync();
    }
}