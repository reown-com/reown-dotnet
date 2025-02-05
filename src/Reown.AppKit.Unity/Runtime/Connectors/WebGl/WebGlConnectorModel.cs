using System;
using Newtonsoft.Json;

namespace Reown.AppKit.Unity
{
#if UNITY_WEBGL
    [Serializable]
    internal class WebGlInitializeParameters
    {
        public string projectId;
        public Core.Metadata metadata;
        public WebGlChain[] supportedChains;
        public string[] includeWalletIds;
        public string[] excludeWalletIds;

        public bool enableEmail;
        public bool enableOnramp;
        public bool enableAnalytics;
        public bool enableCoinbaseWallet;
    }

    [Serializable]
    internal class WebGlChain
    {
        [JsonProperty("id")]
        public long Id { get; }

        [JsonProperty("caipNetworkId")]
        public string CaipNetworkId { get; }

        [JsonProperty("chainNamespace")]
        public string ChainNamespace { get; }

        [JsonProperty("name")]
        public string Name { get; }

        [JsonProperty("nativeCurrency")]
        public Currency NativeCurrency { get; }

        [JsonProperty("rpcUrls")]
        public GenericDefault<RpcUrls> RpcUrls { get; }

        [JsonProperty("blockExplorers")]
        public GenericDefault<BlockExplorer> BlockExplorers { get; }


        public WebGlChain(Chain chain)
        {
            Id = long.Parse(chain.ChainReference);
            CaipNetworkId = chain.ChainId;
            ChainNamespace = chain.ChainNamespace;
            Name = chain.Name;
            NativeCurrency = chain.NativeCurrency;
            RpcUrls = new GenericDefault<RpcUrls>
            {
                @default = new RpcUrls
                {
                    http = new[]
                    {
                        chain.RpcUrl
                    }
                }
            };
            BlockExplorers = new GenericDefault<BlockExplorer>
            {
                @default = chain.BlockExplorer
            };
        }
    }

    [Serializable]
    internal class GenericDefault<T>
    {
        [JsonProperty("default")]
        public T @default;
    }

    [Serializable]
    internal class RpcUrls
    {
        [JsonProperty("http")]
        public string[] http;
    }
#endif
}