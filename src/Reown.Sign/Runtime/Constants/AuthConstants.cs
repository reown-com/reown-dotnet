namespace Reown.Sign.Constants
{
    public static class AuthConstants
    {
        public const string AuthProtocol = "wc";
        public const double AuthVersion = 1.5;
        public const string AuthContext = "auth";
        public const string AuthKeysContext = "authKeys";
        public const string AuthPairingTopicContext = "pairingTopics";
        public const string AuthPendingRequestContext = "requests";

        public static readonly string AuthStoragePrefix = $"{AuthProtocol}@{AuthVersion}:{AuthContext}:";
        public static readonly string AuthPublicKeyName = $"{AuthStoragePrefix}:PUB_KEY";
    }
}