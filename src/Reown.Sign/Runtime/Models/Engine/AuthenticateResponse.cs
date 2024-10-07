#nullable enable

using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Models.Cacao;

namespace Reown.Sign.Models.Engine
{
    [RpcResponseOptions(Clock.ONE_MINUTE, 1117)]
    public class AuthenticateResponse
    {
        public CacaoObject[]? Cacaos;

        public Participant Responder;
    }
}