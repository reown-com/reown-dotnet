using System;
using Reown.Core.Common;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;

namespace Reown.Core.Network
{
    public class RelayUrlBuilder : IRelayUrlBuilder
    {
        public virtual string FormatRelayRpcUrl(string relayUrl, string protocol, string version, string projectId,
            string auth)
        {
            var splitUrl = relayUrl.Split("?");
            var ua = BuildUserAgent(protocol, version);

            var currentParameters = UrlUtils.ParseQs(splitUrl.Length > 1 ? splitUrl[1] : "");

            currentParameters.Add("auth", auth);
            currentParameters.Add("projectId", projectId);
            currentParameters.Add("ua", ua);

            if (TryGetBundleId(out var bundleId))
            {
                currentParameters.Add("bundleId", bundleId);
            }
            else if (TryGetPackageName(out var packageName))
            {
                currentParameters.Add("packageName", packageName);
            }
            else if (TryGetOrigin(out var origin))
            {
                currentParameters.Add("origin", origin);
            }

            var formattedParameters = UrlUtils.StringifyQs(currentParameters);

            return splitUrl[0] + formattedParameters;
        }

        public virtual string BuildUserAgent(string protocol, string version)
        {
            var (os, osVersion) = GetOsInfo();
            var (sdkName, sdkVersion) = GetSdkInfo();

            return $"{protocol}-{version}/{sdkName}-{sdkVersion}/{os}-{osVersion}";
        }

        public virtual (string name, string version) GetOsInfo()
        {
            var name = Environment.OSVersion.Platform.ToString().ToLowerInvariant();
            var version = Environment.OSVersion.Version.ToString().ToLowerInvariant();
            return (name, version);
        }

        public virtual (string name, string version) GetSdkInfo()
        {
            return ("reown-dotnet", SDKConstants.SDK_VERSION);
        }

        protected virtual bool TryGetBundleId(out string bundleId)
        {
            bundleId = null;
            return false;
        }

        protected virtual bool TryGetPackageName(out string packageName)
        {
            packageName = null;
            return false;
        }

        protected virtual bool TryGetOrigin(out string origin)
        {
            origin = null;
            return false;
        }
    }
}