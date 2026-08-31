using Reown.Core.Crypto.Models;

namespace Reown.Core.Models
{
    /// <summary>
    ///     Options for sending a typed request message. Every value is optional, and defaults to the
    ///     behaviour a request gets when no options are given at all.
    /// </summary>
    public class SendRequestOptions
    {
        /// <summary>
        ///     The id to send the request with. When null, the id is derived from the contents of the request
        ///     parameters. Set this when a response listener has to be registered before the request is
        ///     published, which requires knowing the id up front; obtain the value from
        ///     <see cref="Reown.Core.Interfaces.ITypedMessageHandler.GenerateRequestId{T}(T)" /> so that it
        ///     matches the id the request would otherwise have been sent with.
        /// </summary>
        public long? RequestId { get; set; }

        /// <summary>
        ///     How long the request lives for, in seconds. When null, the lifetime is taken from the
        ///     <see cref="Reown.Core.Network.Models.RpcRequestOptionsAttribute" /> on the request type.
        /// </summary>
        public long? Expiry { get; set; }

        /// <summary>
        ///     Crypto encoding options for the published message. When null, the default encoding for the
        ///     topic is used.
        /// </summary>
        public EncodeOptions EncodeOptions { get; set; }
    }
}
