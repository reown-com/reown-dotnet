using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.AppKit.Unity.Http;
using Reown.AppKit.Unity.Model;

namespace Reown.AppKit.Unity
{
    public class ApiController
    {
        private const string BasePath = "https://api.web3modal.com/";
        private const int TimoutSeconds = 5;

        private readonly Lazy<string> _includedWalletIdsString = new(() => AppKit.Config.includedWalletIds is { Length: > 0 }
            ? string.Join(",", AppKit.Config.includedWalletIds)
            : null);

        private readonly Lazy<string> _excludedWalletIdsString = new(() => AppKit.Config.excludedWalletIds is { Length: > 0 }
            ? string.Join(",", AppKit.Config.excludedWalletIds)
            : null);

        private readonly Lazy<string> _chainsString = new(() => AppKit.Config.supportedChains is { Length: > 0 }
            ? string.Join(",", AppKit.Config.supportedChains.Select(x => x.ChainId))
            : null);

        private readonly UnityHttpClient _httpClient = new(new Uri(BasePath), TimeSpan.FromSeconds(TimoutSeconds),
            new AppKitApiHeaderDecorator()
        );

        private const string Platform =
#if UNITY_ANDROID
            "android";
#elif UNITY_IOS
            "ios";
#else
            null;
#endif

        private static void ValidatePaginationParameters(int page, int count)
        {
            if (page < 1)
                throw new ArgumentOutOfRangeException(nameof(page), "Page must be greater than 0");
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than 0");
        }

        public async Task<GetWalletsResponse> GetWallets(
            int page,
            int count,
            string search = null,
            string[] includedWalletIds = null,
            string[] excludedWalletIds = null,
            string[] chains = null)
        {
            ValidatePaginationParameters(page, count);

            var parameters = new Dictionary<string, string>
            {
                ["page"] = page.ToString(),
                ["entries"] = count.ToString(),
                ["platform"] = Platform
            };

            if (search != null)
                parameters["search"] = search;

            parameters["include"] = includedWalletIds?.Length > 0
                ? string.Join(",", includedWalletIds)
                : _includedWalletIdsString.Value;

            parameters["exclude"] = excludedWalletIds?.Length > 0
                ? string.Join(",", excludedWalletIds)
                : _excludedWalletIdsString.Value;

            parameters["chains"] = chains?.Length > 0
                ? string.Join(",", chains)
                : _chainsString.Value;

            return await _httpClient.GetAsync<GetWalletsResponse>("getWallets", parameters);
        }

        public async Task<ApiGetAnalyticsConfigResponse> GetAnalyticsConfigAsync()
        {
            return await _httpClient.GetAsync<ApiGetAnalyticsConfigResponse>("getAnalyticsConfig");
        }
    }

    public class ApiGetAnalyticsConfigResponse
    {
        [JsonProperty("isAnalyticsEnabled")]
        public bool IsAnalyticsEnabled { get; set; }

        [JsonProperty("isAppKitAuthEnabled")]
        public bool IsAppKitAuthEnabled { get; set; }
    }
}