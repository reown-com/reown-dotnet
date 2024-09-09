namespace Reown.Core.Crypto.Encoder
{
    internal static class Bases
    {
        public static Codec Base10 = new(
            "base10",
            "9",
            new BaseX(
                "0123456789",
                "base10"
            )
        );
    }
}