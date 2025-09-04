using System;
using System.Threading.Tasks;
using Reown.Sign.Unity;

namespace Reown.AppKit.Unity.Solana
{
    public abstract class SolanaService
    {
        public ValueTask InitializeAsync(SignClientUnity signClient)
        {
            return InitializeAsyncCore(signClient);
        }

        // -- Sign Message ---------------------------------------------

        public ValueTask<string> SignMessageAsync(string message, string pubkey = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));

            pubkey ??= AppKit.Account.Address;

            return SignMessageAsyncCore(message, pubkey);
        }

        // -- Verify Message -------------------------------------------

        public ValueTask<bool> VerifyMessageSignatureAsync(string message, string signature, string pubkey = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(signature))
                throw new ArgumentNullException(nameof(signature));

            return VerifyMessageSignatureAsyncCore(message, signature, pubkey);
        }


        protected abstract ValueTask InitializeAsyncCore(SignClientUnity signClient);
        protected abstract ValueTask<string> SignMessageAsyncCore(string message, string pubkey);
        protected abstract ValueTask<bool> VerifyMessageSignatureAsyncCore(string message, string signature, string pubkey);
    }
}