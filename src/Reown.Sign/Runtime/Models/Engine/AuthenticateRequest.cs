namespace Reown.Sign.Models.Engine
{
    public class AuthenticateRequest
    {
        public Participant Requester;

        public AuthPayloadParams Payload;

        public long Expiry;
    }
}