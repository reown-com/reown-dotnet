using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Nethereum.Signer;
using Nethereum.Util;
using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Eth;
using Reown.Core.Network.Models;
using Reown.Sign.Models.Cacao;

namespace Reown.Sign.Utils
{
    public static class SignatureUtils
    {
        public const string DefaultRpcUrl = "https://rpc.walletconnect.org/v1";

        public static async Task<bool> VerifySignature(
            string address,
            string reconstructedMessage,
            CacaoSignature cacaoSignature,
            string chainId,
            string projectId,
            string rpcUrl = null)
        {
            if (string.IsNullOrWhiteSpace(cacaoSignature.S))
                throw new ArgumentException("VerifySignature Failed: CacaoSignature S is null or empty");
            
            return cacaoSignature.T switch
            {
                CacaoSignatureType.Eip191 => IsValidEip191Signature(address, reconstructedMessage, cacaoSignature.S),
                CacaoSignatureType.Eip1271 => await IsValidEip1271Signature(address, reconstructedMessage, cacaoSignature.S, chainId, projectId, rpcUrl),
                _ => throw new ArgumentException($"VerifySignature Failed: Attempted to verify CacaoSignature with unknown type {cacaoSignature.T}")
            };
        }

        private static bool IsValidEip191Signature(string address, string reconstructedMessage, string cacaoSignature)
        {
            var signer = new EthereumMessageSigner();
            var recoveredAddress = signer.EncodeUTF8AndEcRecover(reconstructedMessage, cacaoSignature);
            return recoveredAddress.IsTheSameAddress(address);
        }

        private static async Task<bool> IsValidEip1271Signature(
            string address,
            string reconstructedMessage,
            string cacaoSignatureS,
            string chainId,
            string projectId,
            string rpcUrl = null)
        {
            if (!Core.Utils.IsValidChainId(chainId))
                throw new FormatException($"Chain Id doesn't satisfy the CAIP-2 format. chainId: {chainId}");
            
            const string eip1271MagicValue = "0x1626ba7e";
            const string dynamicTypeOffset = "0000000000000000000000000000000000000000000000000000000000000040";
            const string dynamicTypeLength = "0000000000000000000000000000000000000000000000000000000000000041";
            var nonPrefixedSignature = cacaoSignatureS[2..];
            var signer = new EthereumMessageSigner();
            var nonPrefixedHashedMessage = signer.HashPrefixedMessage(Encoding.UTF8.GetBytes(reconstructedMessage)).ToHex();

            var data =
                eip1271MagicValue +
                nonPrefixedHashedMessage +
                dynamicTypeOffset +
                dynamicTypeLength +
                nonPrefixedSignature;

            string result;
            using (var client = new HttpClient())
            {
                var uri = rpcUrl;
                if (string.IsNullOrWhiteSpace(uri))
                {
                    var builder = new UriBuilder(DefaultRpcUrl)
                    {
                        Query = $"chainId={chainId}&projectId={projectId}"
                    };
                    uri = builder.Uri.ToString();
                }
                
                var rpcRequest = new JsonRpcRequest<object[]>("eth_call", new object[]
                {
                    new EthCall
                    {
                        To = address,
                        Data = data
                    },
                    "latest"
                });

                var httpResponse = await client.PostAsync(uri, new StringContent(JsonConvert.SerializeObject(rpcRequest)));

                if (httpResponse is not { IsSuccessStatusCode: true })
                {
                    throw new HttpRequestException($"Failed to call RPC endpoint {uri} with status code {httpResponse?.StatusCode}");
                }

                var jsonResponse = await httpResponse.Content.ReadAsStringAsync();

                var response = JsonConvert.DeserializeObject<JsonRpcResponse<string>>(jsonResponse);

                if (response == null)
                {
                    throw new JsonSerializationException($"Could not deserialize JsonRpcResponse from JSON {jsonResponse}");
                }

                result = response.Result;
            }

            if (string.IsNullOrWhiteSpace(result) || result == "0x")
                return false;

            var recoveredValue = result[..eip1271MagicValue.Length];
            return string.Equals(recoveredValue, eip1271MagicValue, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}