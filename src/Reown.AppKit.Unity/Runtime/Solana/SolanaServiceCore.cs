using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Crypto.Encoder;
using Reown.Sign.Unity;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Reown.AppKit.Unity.Http;
using Reown.Core.Network.Models;
using UnityEngine;

namespace Reown.AppKit.Unity.Solana
{
    public class SolanaServiceCore : SolanaService
    {
        private SignClientUnity _signClient;
        private UnityHttpClient _httpClient;

        protected override ValueTask InitializeAsyncCore(SignClientUnity signClient)
        {
            _signClient = signClient;
            _httpClient = new UnityHttpClient();
            return default;
        }

        protected override async ValueTask<BigInteger> GetBalanceAsyncCore(string pubkey)
        {
            var result = await RpcRequestAsync<GetBalanceResponse>("getBalance", pubkey);
            return result.Value;
        }

        protected override async ValueTask<string> SignMessageAsyncCore(string message, string pubkey)
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            return await SignMessageAsyncCore(messageBytes, pubkey);
        }
        
        protected override async ValueTask<string> SignMessageAsyncCore(byte[] message, string pubkey)
        {
            var messageEncoded = Base58Encoding.Encode(message);

            var request = new SignMessageRequest
            {
                MessageBase58 = messageEncoded,
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

        protected override async ValueTask<SignTransactionResponse> SignTransactionAsyncCore(string transactionBase64, string pubkey)
        {
            var payload = new SignTransactionRequest
            {
                TransactionBase58 = transactionBase64
            };
            return await _signClient.RequestAsync<SignTransactionRequest, SignTransactionResponse>("solana_signTransaction", payload);
        }
        
        protected override async ValueTask<SignAllTransactionsResponse> SignAllTransactionsAsyncCore(string[] transactionsBase64, string pubkey)
        {
            var payload = new SignAllTransactionsRequest
            {
                TransactionsBase58 = transactionsBase64
            };
            return await _signClient.RequestAsync<SignAllTransactionsRequest, SignAllTransactionsResponse>("solana_signAllTransactions", payload);
        }

        protected override async Task<TResult> RpcRequestAsyncCore<TResult>(string method, params object[] parameters)
        {
            var defaultSessionNamespaces = _signClient.AddressProvider.DefaultSession.Namespaces;
            if (defaultSessionNamespaces.TryGetValue("solana", out var solanaNamespace) && solanaNamespace.Methods.Contains(method))
            {
                return parameters.Length == 1
                    ? await _signClient.RequestAsync<object, TResult>(method, parameters[0])
                    : await _signClient.RequestAsync<object[], TResult>(method, parameters);
            }
            
            var request = new JsonRpcRequest<object[]>(method, parameters);
            var rpcUrl = CreateRpcUrl(AppKit.Account.ChainId);
            var response = await _httpClient.PostAsync<JsonRpcResponse<TResult>>(rpcUrl, JsonConvert.SerializeObject(request));
            return response.Result;
        }
    }
}