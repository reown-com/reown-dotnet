using System;
using System.Globalization;

namespace Reown.Sign.Utils
{
    public static class CacaoUtils
    {
        public static string[] ExtractDidAddressSegments(string iss)
        {
            return string.IsNullOrWhiteSpace(iss) ? null : iss.Split(":");
        }

        /// <summary>
        ///     Extracts CAIP-2 Chain ID reference from the issuer string.
        ///     For example: did:pkh:eip155:1:0x3613699A6c5D8BC97a08805876c8005543125F09 -> 1
        /// </summary>
        public static string ExtractDidChainIdReference(string iss)
        {
            return string.IsNullOrWhiteSpace(iss) ? null : ExtractDidAddressSegments(iss)[3];
        }

        /// <summary>
        ///     Extracts CAIP-2 Chain ID from the issuer string.
        ///     For example: did:pkh:eip155:1:0x3613699A6c5D8BC97a08805876c8005543125F09 -> eip155:1
        /// </summary>
        public static string ExtractDidChainId(string iss)
        {
            if (string.IsNullOrWhiteSpace(iss))
                return null;

            var segments = ExtractDidAddressSegments(iss);

            return $"{segments[2]}:{segments[3]}";
        }

        public static string ExtractDidAddress(string iss)
        {
            if (string.IsNullOrWhiteSpace(iss))
                return null;

            var segments = ExtractDidAddressSegments(iss);

            return segments.Length == 0 ? null : segments[^1];
        }

        public static string ToRfc3339(this DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        }
    }
}