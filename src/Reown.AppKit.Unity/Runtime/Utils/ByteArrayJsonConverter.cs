using System;
using Newtonsoft.Json;
using Reown.Core.Common.Utils;

namespace Reown.AppKit.Unity.Utils
{
    /// <summary>
    ///     Converts byte array to hex string and vice versa.
    /// </summary>
    /// <remarks>
    ///     The default behavior of Newtonsoft.Json is to convert byte arrays to base64 strings.
    /// </remarks>
    public class ByteArrayJsonConverter : JsonConverter<byte[]>
    {
        public override byte[] ReadJson(JsonReader reader, Type objectType, byte[] existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Null:
                    return null;
                case JsonToken.String:
                {
                    var hexString = (string)reader.Value;
                    return hexString.HexToByteArray();
                }
                default:
                    throw new JsonSerializationException("Expected byte array object value");
            }
        }

        public override void WriteJson(JsonWriter writer, byte[] value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var hexString = value.ToHex(true);
            writer.WriteValue(hexString);
        }
    }
}