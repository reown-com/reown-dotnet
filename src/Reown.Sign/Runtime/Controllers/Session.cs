using Reown.Core.Controllers;
using Reown.Core.Interfaces;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Controllers
{
    /// <summary>
    ///     A <see cref="Store{TKey,TValue}" /> module for storing
    ///     <see cref="SessionStruct" /> data. This will be used
    ///     for storing session data
    /// </summary>
    public class Session : Store<string, SessionStruct>, ISession
    {
        /// <summary>
        ///     Create a new instance of this module
        /// </summary>
        /// <param name="core">The <see cref="ICore" /> instance that will be used for <see cref="ICore.Storage" /></param>
        public Session(ICore core) : base(core, "session", SignClient.StoragePrefix)
        {
        }
    }
}