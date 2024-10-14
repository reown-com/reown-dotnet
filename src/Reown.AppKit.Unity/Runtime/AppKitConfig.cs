using System;

namespace Reown.AppKit.Unity
{
    [Serializable]
    public class AppKitConfig
    {
        public string[] includedWalletIds;
        public string[] excludedWalletIds;

        public ushort connectViewWalletsCountMobile = 3;
        public ushort connectViewWalletsCountDesktop = 2;

        public bool enableOnramp = true; // Currently supported only in WebGL
        public bool enableAnalytics = true;
        public bool enableCoinbaseWallet = true; // Currently supported only in WebGL

        public Chain[] supportedChains =
        {
            ChainConstants.Chains.Ethereum,
            ChainConstants.Chains.Arbitrum,
            ChainConstants.Chains.Polygon,
            ChainConstants.Chains.Avalanche,
            ChainConstants.Chains.Optimism,
            ChainConstants.Chains.Base,
            ChainConstants.Chains.Celo,
            ChainConstants.Chains.Ronin
        };

        public readonly Metadata Metadata;
        public readonly string ProjectId;

        public AppKitConfig(string projectId, Metadata metadata)
        {
            ProjectId = projectId;
            Metadata = metadata;
        }
    }

    public class Metadata
    {
        public readonly string Name;
        public readonly string Description;
        public readonly string Url;
        public readonly string IconUrl;
        public readonly RedirectData Redirect;

        public Metadata(string name, string description, string url, string iconUrl, RedirectData redirect = null)
        {
            Name = name;
            Description = description;
            Url = url;
            IconUrl = iconUrl;
            Redirect = redirect ?? new RedirectData();
        }
        
        public static implicit operator Reown.Core.Metadata(Metadata metadata)
        {
            return new Reown.Core.Metadata
            {
                Name = metadata.Name,
                Description = metadata.Description,
                Url = metadata.Url,
                Icons = new[] { metadata.IconUrl },
                Redirect = new Reown.Core.Models.RedirectData
                {
                    Native = metadata.Redirect.Native,
                    Universal = metadata.Redirect.Universal
                }
            };
        }
    }
    
    public class RedirectData
    {
        public string Native = string.Empty;
        public string Universal = string.Empty;
    }
}