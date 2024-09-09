using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Reown.Core.Common.Model.Relay
{
    /// <summary>
    ///     A class that defines the different RPC methods for a
    ///     given pub/sub protocol
    /// </summary>
    public abstract class RelayProtocols
    {
        /// <summary>
        ///     The default protocol as a string
        /// </summary>
        public static readonly string Default = "irn";

        /// <summary>
        ///     The Waku protocol definitions
        /// </summary>
        public static RelayProtocols Waku = new WakuRelayProtocol();

        /// <summary>
        ///     The Irn protocol definitions
        /// </summary>
        public static RelayProtocols Irn = new IrnRelayProtocol();

        /// <summary>
        ///     The Iridium protocol definitions
        /// </summary>
        public static RelayProtocols Iridium = new IridiumRelayProtocol();

        private static readonly Dictionary<string, RelayProtocols> _protocols = new()
        {
            { "waku", Waku },
            { "irn", Irn },
            { "iridium", Iridium }
        };

        /// <summary>
        ///     A mapping of protocol names => Protocol Definitions
        /// </summary>
        public static IReadOnlyDictionary<string, RelayProtocols> Protocols
        {
            get => _protocols;
        }

        /// <summary>
        ///     The Publish action RPC method name
        /// </summary>
        [JsonProperty("publish")]
        public abstract string Publish { get; }

        public abstract string BatchPublish { get; }

        /// <summary>
        ///     The Subscribe action RPC method name
        /// </summary>
        [JsonProperty("subscribe")]
        public abstract string Subscribe { get; }

        public abstract string BatchSubscribe { get; }

        /// <summary>
        ///     The Subscription action RPC method name
        /// </summary>
        [JsonProperty("subscription")]
        public abstract string Subscription { get; }

        /// <summary>
        ///     The Unsubscribe action RPC method name
        /// </summary>
        [JsonProperty("unsubscribe")]
        public abstract string Unsubscribe { get; }

        public abstract string BatchUnsubscribe { get; }

        public static RelayProtocols DefaultProtocol
        {
            get => GetRelayProtocol(Default);
        }

        /// <summary>
        ///     Get protocol definitions by the protocol's name
        /// </summary>
        /// <param name="protocol">The protocol name to get definitions for</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException">The protocol doesn't exist</exception>
        public static RelayProtocols GetRelayProtocol(string protocol)
        {
            if (Protocols.ContainsKey(protocol))
                return Protocols[protocol];

            throw new ArgumentException("Relay Protocol not supported: " + protocol);
        }

        /// <summary>
        ///     A class that defines all RelayProtocol definitions for the
        ///     Waku protocol
        /// </summary>
        public class WakuRelayProtocol : RelayProtocols
        {
            public override string Publish
            {
                get => "waku_publish";
            }

            public override string BatchPublish
            {
                get => "waku_batchPublish";
            }

            public override string Subscribe
            {
                get => "waku_subscribe";
            }

            public override string BatchSubscribe
            {
                get => "waku_batchSubscribe";
            }

            public override string Subscription
            {
                get => "waku_subscription";
            }

            public override string Unsubscribe
            {
                get => "waku_unsubscribe";
            }

            public override string BatchUnsubscribe
            {
                get => "waku_batchUnsubscribe";
            }
        }

        /// <summary>
        ///     A class that defines all RelayProtocol definitions for the
        ///     Irn protocol
        /// </summary>
        public class IrnRelayProtocol : RelayProtocols
        {
            public override string Publish
            {
                get => "irn_publish";
            }

            public override string BatchPublish
            {
                get => "irn_batchPublish";
            }

            public override string Subscribe
            {
                get => "irn_subscribe";
            }

            public override string BatchSubscribe
            {
                get => "irn_batchSubscribe";
            }

            public override string Subscription
            {
                get => "irn_subscription";
            }

            public override string Unsubscribe
            {
                get => "irn_unsubscribe";
            }

            public override string BatchUnsubscribe
            {
                get => "irn_batchUnsubscribe";
            }
        }

        /// <summary>
        ///     A class that defines all RelayProtocol definitions for the
        ///     Iridium protocol
        /// </summary>
        public class IridiumRelayProtocol : RelayProtocols
        {
            public override string Publish
            {
                get => "iridium_publish";
            }

            public override string BatchPublish
            {
                get => "iridium_batchPublish";
            }

            public override string Subscribe
            {
                get => "iridium_subscribe";
            }

            public override string BatchSubscribe
            {
                get => "iridium_batchSubscribe";
            }

            public override string Subscription
            {
                get => "iridium_subscription";
            }

            public override string Unsubscribe
            {
                get => "iridium_unsubscribe";
            }

            public override string BatchUnsubscribe
            {
                get => "iridium_batchUnsubscribe";
            }
        }
    }
}