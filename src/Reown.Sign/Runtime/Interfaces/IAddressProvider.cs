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

        SessionStruct DefaultSession { get; set; }

        string DefaultNamespace { get; }

        string DefaultChainId { get; }

        ISession Sessions { get; }
        event EventHandler<DefaultsLoadingEventArgs> DefaultsLoaded;

        Task SetDefaultNamespaceAsync(string @namespace);

        Task SetDefaultChainIdAsync(string chainId);

        Caip25Address CurrentAddress(string chainId = null, SessionStruct session = default);

        IEnumerable<Caip25Address> AllAddresses(string @namespace = null, SessionStruct session = default);

        public Task LoadDefaultsAsync();
    }
}