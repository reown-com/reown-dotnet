#nullable enable

using Reown.Sign.Models.Cacao;

namespace Reown.Sign.Models.Engine
{
    public class AuthenticateResponse
    {
        public CacaoObject[]? Cacaos;

        public Participant Responder;
    }
}