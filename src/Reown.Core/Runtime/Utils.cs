using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Reown.Core.Common.Logging;

namespace Reown.Core
{
    public static class Utils
    {
        private const string SessionIdPattern = @"^[-a-z0-9]{3,8}:[-_a-zA-Z0-9]{1,32}$";
        private static readonly Regex SessionIdRegex = new(SessionIdPattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try
            {
                _ = new Uri(url);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool IsValidChainId(string chainId)
        {
            return SessionIdRegex.IsMatch(chainId);
        }

        public static string ExtractChainReference(string chainId)
        {
            ReadOnlySpan<char> span = chainId;
            var index = span.LastIndexOf(':');
            return index >= 0 ? span[(index + 1)..].ToString() : chainId;
        }

        public static string ExtractChainNamespace(string chainId)
        {
            ReadOnlySpan<char> span = chainId;
            var index = span.LastIndexOf(':');
            return index >= 0 ? span[..index].ToString() : chainId;
        }

        public static (string chainId, string address) DeconstructAccountId(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
                throw new ArgumentException("Provided account id is null or empty");

            var span = accountId.AsSpan();
            var firstColon = span.IndexOf(':');
            var lastColon = span.LastIndexOf(':');

            if (firstColon == -1 || lastColon == -1 || firstColon == lastColon)
                throw new ArgumentException("Invalid account id");

            var chainId = span[..lastColon].ToString();
            var address = span[(lastColon + 1)..].ToString();
            return (chainId, address);
        }

        public static bool IsValidAccountId(string accountId)
        {
            var (chainId, address) = DeconstructAccountId(accountId);
            return !string.IsNullOrWhiteSpace(address) && IsValidChainId(chainId);
        }

        public static bool IsValidRequestExpiry(long expiry, long min, long max)
        {
            return expiry <= max && expiry >= min;
        }

        public static IEnumerable<IEnumerable<TSource>> Batch<TSource>(this IEnumerable<TSource> source, int size)
        {
            TSource[] bucket = null;
            var count = 0;

            foreach (var item in source)
            {
                if (bucket == null)
                    bucket = new TSource[size];

                bucket[count++] = item;
                if (count != size)
                    continue;

                yield return bucket;

                bucket = null;
                count = 0;
            }

            if (bucket != null && count > 0)
                yield return bucket.Take(count).ToArray();
        }
    }
}