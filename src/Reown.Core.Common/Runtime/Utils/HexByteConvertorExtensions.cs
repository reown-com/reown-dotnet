using System;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Reown.Core.Common.Utils
{
    public static class HexByteConvertorExtensions
    {
        private static readonly byte[] Empty = Array.Empty<byte>();

        public static string ToHex(this byte[] value, bool prefix = false)
        {
            var prefixLength = prefix ? 2 : 0;
            var bufferLength = prefixLength + value.Length * 2; // Each byte becomes two hex characters

            var buffer = bufferLength <= 256
                ? stackalloc char[bufferLength]
                : new char[bufferLength];

            var index = 0;
            if (prefix)
            {
                buffer[0] = '0';
                buffer[1] = 'x';
                index = 2;
            }

            foreach (var b in value)
            {
                buffer[index++] = GetHexChar(b >> 4); // High nibble
                buffer[index++] = GetHexChar(b & 0x0F); // Low nibble
            }

            return new string(buffer);
        }
        
        public static string ToHex(this string value, bool prefix = false)
        {
            return ToHex(Encoding.UTF8.GetBytes(value), prefix);
        }

        public static string ToHex(this int value, bool prefix = false)
        {
            Span<char> buffer = stackalloc char[10]; // 8 characters for the value + 2 for the prefix

            var index = 0;
            if (prefix)
            {
                buffer[0] = '0';
                buffer[1] = 'x';
                index = 2;
            }

            var success = value.TryFormat(buffer[index..], out var charsWritten, "x");

            if (!success)
            {
                throw new InvalidOperationException("Failed to convert value to hex");
            }

            return new string(buffer[..(index + charsWritten)]);
        }

        public static string ToHex(this BigInteger value, bool prefix = false)
        {
            if (value.IsZero)
                return prefix ? "0x0" : "0";

            var byteCount = value.GetByteCount();
            var prefixLength = prefix ? 2 : 0;
            var bufferLength = prefixLength + byteCount * 2; // Each byte becomes two hex characters

            var buffer = bufferLength <= 256
                ? stackalloc char[bufferLength]
                : new char[bufferLength];

            var success = value.TryFormat(buffer, out var charsWritten, "x");
            if (!success)
            {
                throw new InvalidOperationException("Failed to convert value to hexadecimal.");
            }

            // Remove unnecessary leading zeros
            var nonZeroStartIndex = 0;
            while (nonZeroStartIndex < charsWritten && buffer[nonZeroStartIndex] == '0')
                nonZeroStartIndex++;

            if (nonZeroStartIndex == charsWritten)
                return prefix ? "0x0" : "0";

            if (!prefix)
                return new string(buffer.Slice(nonZeroStartIndex, charsWritten - nonZeroStartIndex));

            var resultLength = charsWritten - nonZeroStartIndex;
            var resultBuffer = bufferLength <= 256 ? stackalloc char[2 + resultLength] : new char[2 + resultLength];
            resultBuffer[0] = '0';
            resultBuffer[1] = 'x';
            buffer.Slice(nonZeroStartIndex, resultLength).CopyTo(resultBuffer[2..]);
            return new string(resultBuffer);
        }

        public static bool HasHexPrefix(this string value)
        {
            return value.StartsWith("0x");
        }

        public static bool IsHex(this string value)
        {
            bool isHex;
            foreach (var c in value.RemoveHexPrefix())
            {
                isHex = c is >= '0' and <= '9' ||
                        c is >= 'a' and <= 'f' ||
                        c is >= 'A' and <= 'F';

                if (!isHex)
                    return false;
            }

            return true;
        }

        public static string RemoveHexPrefix(this string value)
        {
            return value.Substring(value.StartsWith("0x") ? 2 : 0);
        }

        public static bool IsTheSameHex(this string first, string second)
        {
            return string.Equals(EnsureHexPrefix(first).ToLower(), EnsureHexPrefix(second).ToLower(),
                StringComparison.Ordinal);
        }

        public static string EnsureHexPrefix(this string value)
        {
            if (value == null) return null;
            if (!value.HasHexPrefix())
                return "0x" + value;
            return value;
        }

        public static string[] EnsureHexPrefix(this string[] values)
        {
            if (values != null)
                foreach (var value in values)
                    value.EnsureHexPrefix();
            return values;
        }

        public static string ToHexCompact(this byte[] value)
        {
            return ToHex(value).TrimStart('0');
        }

        private static byte[] HexToByteArrayInternal(string value)
        {
            byte[] bytes = null;
            if (string.IsNullOrEmpty(value))
            {
                bytes = Empty;
            }
            else
            {
                var stringLength = value.Length;
                var characterIndex = value.StartsWith("0x", StringComparison.Ordinal) ? 2 : 0;
                // Does the string define leading HEX indicator '0x'. Adjust starting index accordingly.               
                var numberOfCharacters = stringLength - characterIndex;

                var addLeadingZero = false;
                if (0 != numberOfCharacters % 2)
                {
                    addLeadingZero = true;

                    numberOfCharacters += 1; // Leading '0' has been striped from the string presentation.
                }

                bytes = new byte[numberOfCharacters / 2]; // Initialize our byte array to hold the converted string.

                var writeIndex = 0;
                if (addLeadingZero)
                {
                    bytes[writeIndex++] = FromCharacterToByte(value[characterIndex], characterIndex);
                    characterIndex += 1;
                }

                for (var readIndex = characterIndex; readIndex < value.Length; readIndex += 2)
                {
                    var upper = FromCharacterToByte(value[readIndex], readIndex, 4);
                    var lower = FromCharacterToByte(value[readIndex + 1], readIndex + 1);

                    bytes[writeIndex++] = (byte)(upper | lower);
                }
            }

            return bytes;
        }

        public static byte[] HexToByteArray(this string value)
        {
            try
            {
                return HexToByteArrayInternal(value);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"String '{value}' could not be converted to byte array (not hex?).", ex);
            }
        }

        // Maps 0-15 to '0'-'9' and 'a'-'f'
        private static char GetHexChar(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + (value - 10));
        }

        private static byte FromCharacterToByte(char character, int index, int shift = 0)
        {
            var value = (byte)character;
            if (value is > 0x40 and < 0x47 or > 0x60 and < 0x67)
            {
                if (0x40 == (0x40 & value))
                    if (0x20 == (0x20 & value))
                        value = (byte)(value + 0xA - 0x61 << shift);
                    else
                        value = (byte)(value + 0xA - 0x41 << shift);
            }
            else if (0x29 < value && 0x40 > value)
            {
                value = (byte)(value - 0x30 << shift);
            }
            else
            {
                throw new FormatException($"Character '{character}' at index '{index}' is not valid alphanumeric character.");
            }

            return value;
        }
    }
}