using Newtonsoft.Json;

namespace Reown.Core.Network.Models
{
    /// <summary>
    ///     Represents a full JSON RPC response with the given result type of T
    /// </summary>
    /// <typeparam name="T">The type of the result property for this JSON RPC response</typeparam>
    public class JsonRpcResponse<T> : IJsonRpcResult<T>
    {
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        private Error _error;

        [JsonProperty("id")]
        private long _id;

        [JsonProperty("jsonrpc")]
        private string _jsonRPC = "2.0";


        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        private T _result;

        /// <summary>
        ///     Create a blank Json rpc response
        /// </summary>
        [JsonConstructor]
        public JsonRpcResponse()
        {
        }

        /// <summary>
        ///     Create a new Json rpc response with the given id, error and result
        ///     parameters.
        /// </summary>
        /// <param name="id">The id of this json response</param>
        /// <param name="error">The error of this json response, if one is present</param>
        /// <param name="result">The result of this json response, if one is present</param>
        public JsonRpcResponse(long id, Error error, T result)
        {
            _id = id;
            _error = error;
            _result = result;
        }

        /// <summary>
        ///     Whether or not this response is an error response
        /// </summary>
        [JsonIgnore]
        public bool IsError
        {
            get => Error != null;
        }

        /// <summary>
        ///     The id of this Json rpc response, should match the original request
        /// </summary>
        [JsonIgnore]
        public long Id
        {
            get => _id;
            set => _id = value;
        }

        /// <summary>
        ///     The JSON RPC version for this payload
        /// </summary>
        [JsonIgnore]
        public string JsonRPC
        {
            get => _jsonRPC;
        }

        /// <summary>
        ///     The error field
        /// </summary>
        [JsonIgnore]
        public Error Error
        {
            get => _error;
        }

        /// <summary>
        ///     The result field for this response, if one is present
        /// </summary>
        [JsonIgnore]
        public T Result
        {
            get => _result;
            set => _result = value;
        }
    }
}