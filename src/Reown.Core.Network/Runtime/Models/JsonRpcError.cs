using Newtonsoft.Json;

namespace Reown.Core.Network.Models
{
    /// <summary>
    ///     A Json RPC response containing an error
    /// </summary>
    public class JsonRpcError : IJsonRpcError
    {
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        private Error _error;

        /// <summary>
        ///     The id field
        /// </summary>
        [JsonProperty("id")]
        private long _id;

        [JsonProperty("jsonrpc")]
        private string _jsonRpc = "2.0";

        /// <summary>
        ///     Create a blank JSON rpc error response
        /// </summary>
        public JsonRpcError()
        {
        }

        /// <summary>
        ///     Create a JSON rpc error response with the given id and ErrorResponse
        /// </summary>
        /// <param name="id">The id of the response</param>
        /// <param name="error">The error value</param>
        public JsonRpcError(long id, Error error)
        {
            _id = id;
            _error = error;
        }

        [JsonIgnore]
        public long Id
        {
            get => _id;
        }

        /// <summary>
        ///     The jsonrpc field
        /// </summary>
        [JsonIgnore]
        public string JsonRPC
        {
            get => _jsonRpc;
        }

        /// <summary>
        ///     The error field
        /// </summary>
        [JsonIgnore]
        public Error Error
        {
            get => _error;
        }
    }
}