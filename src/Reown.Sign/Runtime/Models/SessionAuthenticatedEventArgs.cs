using System;
using Reown.Sign.Models.Cacao;

namespace Reown.Sign.Models
{
    public class SessionAuthenticatedEventArgs : EventArgs
    {
        public CacaoObject[] Auths;
        public Session Session;
    }
}