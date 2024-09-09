using System;

namespace Reown.Core.Crypto.Encoder
{
    internal class Codec
    {
        public Codec(string name, string prefix, BaseX baseX) : this(name, prefix, baseX.Encode, baseX.Decode)
        {
        }

        public Codec(string name, string prefix, Func<byte[], string> encoder, Func<string, byte[]> decoder)
        {
            Name = name;
            Prefix = prefix;
            Encoder = encoder;
            Decoder = decoder;
        }

        public string Name { get; }
        public string Prefix { get; }
        public Func<byte[], string> Encoder { get; }
        public Func<string, byte[]> Decoder { get; }

        public string Encode(byte[] bytes)
        {
            return $"{Encoder(bytes)}";
        }

        public byte[] Decode(string source)
        {
            if (source[0] != Prefix[0])
            {
                source = Prefix + source;
            }

            return Decoder(source.Substring(1));
        }
    }
}