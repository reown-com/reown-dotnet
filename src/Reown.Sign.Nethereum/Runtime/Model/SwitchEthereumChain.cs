using System;
using System.Numerics;
using Newtonsoft.Json;
using Reown.Core.Common.Utils;

namespace Reown.Sign.Nethereum.Model
{
    [Serializable]
    public class SwitchEthereumChain
    {
        [JsonProperty("chainId")]
        public string chainId;

        [Preserve]
        public SwitchEthereumChain()
        {
        }

        public SwitchEthereumChain(string chainId)
        {
            // Convert CAIP-2 chainId to Ethereum Chain ID
            if (Core.Utils.IsValidChainId(chainId))
                chainId = Core.Utils.ExtractChainReference(chainId);

            this.chainId = !chainId.StartsWith("0x")
                ? BigInteger.Parse(chainId).ToHex(true)
                : chainId;
        }
    }
}