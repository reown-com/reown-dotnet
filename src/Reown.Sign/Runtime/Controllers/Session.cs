using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    /// <summary>
    ///     A <see cref="Store{TKey,TValue}" /> module for storing
    ///     <see cref="Models.Session" /> data. This will be used
    ///     for storing session data
    /// </summary>
    public class Session : Store<string, Models.Session>, ISession
    {
        /// <summary>
        ///     Create a new instance of this module
        /// </summary>
        /// <param name="coreClient">The <see cref="ICoreClient" /> instance that will be used for <see cref="ICoreClient.Storage" /></param>
        public Session(ICoreClient coreClient) : base(coreClient, "session", SignClient.StoragePrefix)
        {
        }
    }
}