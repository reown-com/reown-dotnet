using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Reown.Core.Crypto.Encoder;
using Reown.Sign.Unity;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

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

        protected override ValueTask<BigInteger> GetBalanceAsyncCore(string pubkey)
        {
            throw new System.NotImplementedException();
            return new ValueTask<BigInteger>(new BigInteger(0));
        }

        protected override async ValueTask<string> SignMessageAsyncCore(string message, string pubkey = null)
        {
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

        protected override Task<TResult> RpcRequestAsyncCore<TResult>(string method, params object[] parameters)
        {
            var defaultSessionNamespaces = _signClient.AddressProvider.DefaultSession.Namespaces;
            if (defaultSessionNamespaces.TryGetValue("solana", out var solanaNamespace) && solanaNamespace.Methods.Contains(method))
            {
                return parameters.Length == 1
                    ? _signClient.RequestAsync<object, TResult>(method, parameters[0])
                    : _signClient.RequestAsync<object[], TResult>(method, parameters);
            }
            else
            {
                throw new System.NotImplementedException();
            }
        }
    }
}