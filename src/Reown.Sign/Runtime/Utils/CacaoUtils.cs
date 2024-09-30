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

        public static string ExtractDidChainId(string iss)
        {
            return string.IsNullOrWhiteSpace(iss) ? null : ExtractDidAddressSegments(iss)[3];
        }

        public static string ExtractNamespacedDidChainId(string iss)
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