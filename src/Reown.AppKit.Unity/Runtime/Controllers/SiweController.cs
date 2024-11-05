using System;
using System.Threading.Tasks;

namespace Reown.AppKit.Unity
{
    public class SiweController
    {
        public bool IsEnabled
        {
            get => Config is { Enabled: true };
        }

        public SiweConfig Config
        {
            get => AppKit.Config.siweConfig;
        }

        public SiweController()
        {
            if (AppKit.Config.siweConfig?.GetMessageParams == null)
            {
                throw new InvalidOperationException("GetMessageParams function is required in SiweConfig.");
            }
        }

        public async ValueTask<string> GetNonceAsync()
        {
            if (Config.GetNonce != null)
            {
                return await Config.GetNonce();
            }

            return SiweUtils.GenerateNonce();
        }

        public async ValueTask<SiweMessage> CreateMessageAsync(string ethAddress, string ethChainId)
        {
            var nonce = await GetNonceAsync();
            var messageParams = AppKit.Config.siweConfig.GetMessageParams();

            var createMessageArgs = new SiweCreateMessageArgs(messageParams)
            {
                Nonce = nonce,
                Address = ethAddress,
                ChainId = ethChainId
            };

            var message = Config.CreateMessage != null
                ? Config.CreateMessage(createMessageArgs)
                : SiweUtils.FormatMessage(createMessageArgs);

            return new SiweMessage
            {
                Message = message,
                CreateMessageArgs = createMessageArgs
            };
        }

        public async ValueTask<bool> VerifyMessageAsync(SiweVerifyMessageArgs args)
        {
            if (Config.VerifyMessage != null)
            {
                return await Config.VerifyMessage(args);
            }

            return await args.Cacao.VerifySignature(AppKit.Config.projectId);
        }
        
        public async ValueTask<SiweSession> GetSessionAsync()
        {
            if (Config.GetSession != null)
            {
                return await Config.GetSession();
            }

            return null;
        }
    }
}