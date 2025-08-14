using System;
using Reown.Core.Network;
using UnityEngine;

namespace Reown.Sign.Unity
{
    public class UnityRelayUrlBuilder : RelayUrlBuilder
    {
        private static string _cachedOsName;
        private static string _cachedOsVersion;

#if UNITY_IOS || UNITY_STANDALONE_OSX
        private static string _cachedBundleId;
#endif

#if UNITY_ANDROID
        private static string _cachedPackageName;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        private static string _cachedOrigin;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void InitializeCaches()
        {
            ComputeOsInfoCache();
            ComputePlatformIdCaches();
        }

        private static void ComputeOsInfoCache()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var splitOS = SystemInfo.operatingSystem.Split(' ');
            _cachedOsName = "android";
            _cachedOsVersion = splitOS[2];
#elif UNITY_IOS && !UNITY_EDITOR
            var splitOS = SystemInfo.operatingSystem.Split(' ');
            // var platform = splitOS[0].ToLower();
            var version = splitOS[1];
            _cachedOsName = "ios";
            _cachedOsVersion = version;
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var osString = SystemInfo.operatingSystem;
            var startIndex = osString.IndexOf("OS X", StringComparison.Ordinal) + 5;
            var version = osString.Substring(startIndex);
            _cachedOsName = "macos";
            _cachedOsVersion = version;
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var splitOS = SystemInfo.operatingSystem.Split(' ');
            var version = splitOS[1]; // e.g. Vista or 11
            var architecture = splitOS[^1]; // e.g. 64bit
            _cachedOsName = "windows";
            _cachedOsVersion = $"{version}-{architecture}";
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
            var splitOS = SystemInfo.operatingSystem.Split(' ');
            if (splitOS.Length < 5)
            {
                _cachedOsName = "linux";
                _cachedOsVersion = "unknown";
            }
            else
            {
                var kernelVersion = splitOS[1];
                var distribution = splitOS[2];
                var architecture = splitOS[splitOS.Length - 1];
                _cachedOsName = "linux";
                _cachedOsVersion = $"{distribution}-{kernelVersion}-{architecture}";
            }
#else
            var baseInfo = new RelayUrlBuilder().GetOsInfo();
            _cachedOsName = baseInfo.name;
            _cachedOsVersion = baseInfo.version;
#endif
        }

        private static void ComputePlatformIdCaches()
        {
#if UNITY_IOS || UNITY_STANDALONE_OSX
            _cachedBundleId = Application.identifier;
#endif

#if UNITY_ANDROID
            _cachedPackageName = Application.identifier;
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            _cachedOrigin = Application.identifier;
#endif
        }

        public override (string name, string version) GetOsInfo()
        {
            return (_cachedOsName, _cachedOsVersion);
        }

        public override (string name, string version) GetSdkInfo()
        {
            return ("reown-unity", SignMetadata.Version);
        }

#pragma warning disable S1185
        protected override bool TryGetBundleId(out string bundleId)
        {
#if UNITY_IOS || UNITY_STANDALONE_OSX
            bundleId = _cachedBundleId;
            return true;
#else
            return base.TryGetBundleId(out bundleId);
#endif
        }

        protected override bool TryGetPackageName(out string packageName)
        {
#if UNITY_ANDROID
            packageName = _cachedPackageName;
            return true;
#else
            return base.TryGetPackageName(out packageName);
#endif
        }

        protected override bool TryGetOrigin(out string origin)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            origin = _cachedOrigin;
            return true;
#else
            return base.TryGetOrigin(out origin);
#endif
        }
    }
#pragma warning restore S1185
}