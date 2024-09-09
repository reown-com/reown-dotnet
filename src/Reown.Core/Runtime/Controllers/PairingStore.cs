using Reown.Core.Interfaces;
using Reown.Core.Models.Pairing;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     A <see cref="Store{TKey,TValue}" /> module for storing
    ///     <see cref="PairingStruct" /> data. This will be used
    ///     for storing pairing data
    /// </summary>
    public class PairingStore : Store<string, PairingStruct>, IPairingStore
    {
        /// <summary>
        ///     Create a new instance of this module
        /// </summary>
        /// <param name="core">The <see cref="ICore" /> instance that will be used for <see cref="ICore.Storage" /></param>
        public PairingStore(ICore core) : base(core, "pairing", Reown.Core.Core.StoragePrefix)
        {
        }
    }
}