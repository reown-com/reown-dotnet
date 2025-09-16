using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.AppKit.Unity.Http;
using Reown.AppKit.Unity.Model.BlockchainApi;
using Reown.AppKit.Unity.Model.Errors;
using Reown.Sign.Interfaces;
using UnityEngine;

namespace Reown.AppKit.Unity
{
    public class BlockchainApiController
    {
        private const string BasePath = "https://rpc.walletconnect.org/v1/";
        private const int TimoutSeconds = 5;

        private readonly IDictionary<string, string> _getBalanceHeaders = new Dictionary<string, string>
        {
            { "x-sdk-version", AppKit.Version }
        };

        private readonly UnityHttpClient _httpClient = new(new Uri(BasePath), TimeSpan.FromSeconds(TimoutSeconds));
        private string _clientIdQueryParam;

        private ISignClient _signClient;

        public Task InitializeAsync(ISignClient signClient)
        {
            _signClient = signClient;
            SetOriginHeader();
            return Task.CompletedTask;
        }

        private void SetOriginHeader()
        {
#if UNITY_IOS || UNITY_STANDALONE_OSX || UNITY_WEBGL || UNITY_ANDROID
            _getBalanceHeaders["origin"] = Application.identifier;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            _getBalanceHeaders["origin"] = "https://windows.web3modal.com";
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            _getBalanceHeaders["origin"] = "https://linux.web3modal.com";
#else
            _getBalanceHeaders["origin"] = "https://unknown-unity.web3modal.com";
#endif
        }

        public static bool IsAccountDataSupported(string chainId)
        {
            var chainNamespace = Core.Utils.ExtractChainNamespace(chainId);
            return chainNamespace == "eip155";
        }

        public async Task<GetIdentityResponse> GetIdentityAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));

            var projectId = AppKit.Config.projectId;

            if (string.IsNullOrWhiteSpace(projectId))
                throw new InvalidOperationException("Project ID is not set");

            var path = $"identity/{address}?projectId={projectId}";

            if (string.IsNullOrWhiteSpace(_clientIdQueryParam) && _signClient != null)
            {
                var rawClientId = await _signClient.CoreClient.Crypto.GetClientId();
                _clientIdQueryParam = $"&clientId={Uri.EscapeDataString(rawClientId)}";
            }

            if (!string.IsNullOrWhiteSpace(_clientIdQueryParam))
                path += _clientIdQueryParam;

            return await _httpClient.GetAsync<GetIdentityResponse>(path);
        }

        public async Task<GetBalanceResponse> GetBalanceAsync(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address));

            var projectId = AppKit.Config.projectId;
            var chainId = AppKit.NetworkController.ActiveChain.ChainId;
            var path = $"account/{address}/balance?projectId={projectId}&currency=usd&chainId={chainId}";
            GetBalanceResponse result;
            try
            {
                result = await _httpClient.GetAsync<GetBalanceResponse>(path, headers: _getBalanceHeaders);
            }
            catch (ReownHttpException e) when (e.StatusCode == 503) // unsupported chain
            {
                result = new GetBalanceResponse(Array.Empty<Balance>());
            }
            return result;
        }
    }
}