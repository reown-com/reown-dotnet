using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    /// <summary>
    ///     A <see cref="Store{TKey,TValue}" /> module for storing
    ///     <see cref="ProposalStruct" /> data. This will be used
    ///     for storing proposal data
    /// </summary>
    public class Proposal : Store<long, ProposalStruct>, IProposal
    {
        /// <summary>
        ///     Create a new instance of this module
        /// </summary>
        /// <param name="coreClient">The <see cref="ICoreClient" /> instance that will be used for <see cref="ICoreClient.Storage" /></param>
        public Proposal(ICoreClient coreClient) : base(coreClient, "proposal", SignClient.StoragePrefix)
        {
        }
    }
}