using Newtonsoft.Json;
using Reown.Core.Network;

namespace Reown.Core.Models.History
{
    /// <summary>
    ///     Describes whether a JSON-RPC history record was created for a request received from a peer or sent to a peer.
    /// </summary>
    public enum JsonRpcRecordDirection
    {
        /// <summary>
        ///     The request was received from a peer and this client is responsible for publishing its response.
        /// </summary>
        Inbound,

        /// <summary>
        ///     The request was sent by this client and its response must be routed when received.
        /// </summary>
        Outbound
    }

    /// <summary>
    ///     A class representing a single JSON RPC history record containing the Id, Topic, Request, Response and ChainId.
    ///     If no Response is set, then the record hasn't been resolved yet
    /// </summary>
    /// <typeparam name="T">The type of the request parameter</typeparam>
    /// <typeparam name="R">The type of the response parameter</typeparam>
    public class JsonRpcRecord<T, R>
    {
        /// <summary>
        ///     The id of the JSON RPC request
        /// </summary>
        [JsonProperty("id")]
        public long Id;

        /// <summary>
        ///     The request data for this JSON RPC record
        /// </summary>
        [JsonProperty("request")]
        public IRequestArguments<T> Request;

        /// <summary>
        ///     The response data for this JSON RPC record. If no Response data is set, then this request is
        ///     still pending
        /// </summary>
        [JsonProperty("response")]
        public IJsonRpcResult<R> Response;

        /// <summary>
        ///     The topic the request was sent in
        /// </summary>
        [JsonProperty("topic")]
        public string Topic;

        /// <summary>
        ///     The direction in which this record's request travelled. A null value represents a record persisted by
        ///     an earlier SDK version, whose direction was not recorded.
        /// </summary>
        [JsonProperty("direction")]
        public JsonRpcRecordDirection? Direction;

        /// <summary>
        ///     The Unix timestamp, in seconds, after which this pending record may be removed. A null value represents
        ///     a record persisted by an earlier SDK version, whose expiry was not recorded.
        /// </summary>
        [JsonProperty("expiry")]
        public long? Expiry;

        /// <summary>
        ///     This constructor is required for the JSON deserializer to be able
        ///     to identify concrete classes to use when deserializing the interface properties.
        /// </summary>
        public JsonRpcRecord(IJsonRpcRequest<T> request)
        {
            Request = request;
        }

        /// <summary>
        ///     The chainId this request is intended for
        /// </summary>
        [JsonProperty("chainId")]
        public string ChainId { get; set; }
    }
}
