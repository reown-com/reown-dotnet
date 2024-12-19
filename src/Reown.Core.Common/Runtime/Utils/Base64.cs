using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Reown.Core.Common.Utils
{
    public static unsafe class Base64
    {
        private static readonly char[] Base64UrlEncodeTable = {
            'A','B','C','D','E','F','G','H','I','J','K','L','M','N','O','P',
            'Q','R','S','T','U','V','W','X','Y','Z','a','b','c','d','e','f',
            'g','h','i','j','k','l','m','n','o','p','q','r','s','t','u','v',
            'w','x','y','z','0','1','2','3','4','5','6','7','8','9','-','_'};

        public static int GetBase64UrlEncodeLength(int length)
        {
            if (length == 0) return 0;
            var mod = length % 3;
            return length / 3 * 4 + (mod == 0 ? 0 : mod + 1);
        }

        public static bool TryToBase64UrlChars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten)
        {
            fixed (byte* inData = &MemoryMarshal.GetReference(bytes))
            fixed (char* outChars = &MemoryMarshal.GetReference(chars))
            {
                charsWritten = EncodeBase64Core(inData, outChars, 0, bytes.Length, Base64UrlEncodeTable, false);
                return true;
            }
        }

        public static string EncodeToBase64UrlString(byte[] bytes)
        {
            var buffer = ArrayPool<char>.Shared.Rent(GetBase64UrlEncodeLength(bytes.Length));
            try
            {
                var bufferSpan = buffer.AsSpan();
                TryToBase64UrlChars(bytes, bufferSpan, out var written);
                return new string(bufferSpan[..written]);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }

        private static int EncodeBase64Core(byte* bytes, char* chars, int offset, int length, char[] encodeTable, bool withPadding)
        {
            var mod3 = length % 3;
            var loopLength = offset + (length - mod3);

            var i = 0;
            var j = 0;

            fixed (char* table = &encodeTable[0])
            {
                for (i = offset; i < loopLength; i += 3)
                {
                    chars[j] = table[(bytes[i] & 0b11111100) >> 2];
                    chars[j + 1] = table[(bytes[i] & 0b00000011) << 4 | (bytes[i + 1] & 0b11110000) >> 4];
                    chars[j + 2] = table[(bytes[i + 1] & 0b00001111) << 2 | (bytes[i + 2] & 0b11000000) >> 6];
                    chars[j + 3] = table[bytes[i + 2] & 0b00111111];
                    j += 4;
                }

                i = loopLength;

                if (mod3 == 2)
                {
                    chars[j] = table[(bytes[i] & 0b11111100) >> 2];
                    chars[j + 1] = table[(bytes[i] & 0b00000011) << 4 | (bytes[i + 1] & 0b11110000) >> 4];
                    chars[j + 2] = table[(bytes[i + 1] & 0b00001111) << 2];
                    if (withPadding)
                    {
                        chars[j + 3] = '=';
                        j += 4;
                    }
                    else
                    {
                        j += 3;
                    }
                }
                else if (mod3 == 1)
                {
                    chars[j] = table[(bytes[i] & 0b11111100) >> 2];
                    chars[j + 1] = table[(bytes[i] & 0b00000011) << 4];
                    if (withPadding)
                    {
                        chars[j + 2] = '=';
                        chars[j + 3] = '=';
                        j += 4;
                    }
                    else
                    {
                        j += 2;
                    }
                }

                return j;
            }
        }
    }
}