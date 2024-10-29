using System.Threading.Tasks;

namespace Reown.AppKit.Unity
{
    public class SiweController
    {
        public SiweConfig Config { get; }

        public bool IsEnabled
        {
            get => Config is { Enabled: true };
        }

        public SiweController(ConnectorController connectorController, SiweConfig config)
        {
            Config = config;
        }

        public async ValueTask<string> GetNonceAsync()
        {
            if (Config.GetNonce != null)
            {
                return await Config.GetNonce();
            }

            return SiweUtils.GenerateNonce();
        }

        public async ValueTask<string> CreateMessageAsync(string ethAddress, string ethChainId)
        {
            var nonce = await GetNonceAsync();
            var messageParams = Config.GetMessageParams();

            var createMessageArgs = new SiweCreateMessageArgs(messageParams)
            {
                Nonce = nonce,
                Address = ethAddress,
                ChainId = ethChainId
            };

            return Config.CreateMessage != null
                ? Config.CreateMessage(createMessageArgs)
                : SiweUtils.FormatMessage(createMessageArgs);
        }
    }
}