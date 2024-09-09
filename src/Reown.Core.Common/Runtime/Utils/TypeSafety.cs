using Newtonsoft.Json;

namespace Reown.Core.Common.Utils
{
    public static class TypeSafety
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            TypeNameHandling = TypeNameHandling.Auto
        };

        public static void EnsureTypeSerializerSafe<T>(T testObject)
        {
            // unwrapping and rewrapping the object
            // to / from JSON should tell us
            // if it's serializer safe, since
            // we are using the serializer to test
            UnsafeJsonRewrap<T, T>(testObject, Settings);
        }

        public static TR UnsafeJsonRewrap<T, TR>(this T source, JsonSerializerSettings settings = null)
        {
            var json = settings == null ? JsonConvert.SerializeObject(source) : JsonConvert.SerializeObject(source, settings);

            return JsonConvert.DeserializeObject<TR>(json);
        }
    }
}