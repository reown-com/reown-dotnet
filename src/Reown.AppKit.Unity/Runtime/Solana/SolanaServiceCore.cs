using System.Text;
using System.Threading.Tasks;
using Reown.Core.Crypto.Encoder;
using Reown.Sign.Unity;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Reown.Core.Common.Logging;

namespace Reown.AppKit.Unity.Solana
{
    public class SolanaServiceCore : SolanaService
    {
        private SignClientUnity _signClient;

        protected override ValueTask InitializeAsyncCore(SignClientUnity signClient)
        {
            _signClient = signClient;
            return default;
        }

        protected override async ValueTask<string> SignMessageAsyncCore(string message, string pubkey = null)
        {
            pubkey ??= AppKit.Account.Address;

            var messageBytes = Encoding.UTF8.GetBytes(message);
            var messageBase58 = Base58Encoding.Encode(messageBytes);

            var request = new SignMessageRequest
            {
                MessageBase58 = messageBase58,
                Pubkey = pubkey
            };

            var response = await _signClient.RequestAsync<SignMessageRequest, SignatureResponse>("solana_signMessage", request);
            return response.Signature;
        }

        protected override ValueTask<bool> VerifyMessageSignatureAsyncCore(string message, string signature, string pubkey)
        {
            pubkey ??= AppKit.Account.Address;

            var messageBytes = Encoding.UTF8.GetBytes(message);
            var signatureBytes = Base58Encoding.Decode(signature);
            var pubkeyBytes = Base58Encoding.Decode(pubkey);

            if (signatureBytes.Length != 64 || pubkeyBytes.Length != 32)
                return new ValueTask<bool>(false);

            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(pubkeyBytes, 0));
            verifier.BlockUpdate(messageBytes, 0, messageBytes.Length);
            return new ValueTask<bool>(verifier.VerifySignature(signatureBytes));
        }
    }
}