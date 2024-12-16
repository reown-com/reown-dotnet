using Reown.Core.Interfaces;
using Reown.Sign.Models;

namespace Reown.Sign.Interfaces
{
    /// <summary>
    ///     A <see cref="IStore{TKey,TValue}" /> interface for a module
    ///     that stores <see cref="Session" /> data.
    /// </summary>
    public interface ISession : IStore<string, Session>
    {
    }
}