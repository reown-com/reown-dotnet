using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Reown.Core.Interfaces;
using Reown.Core.Models.History;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     Creates and tracks <see cref="JsonRpcHistory{T,TR}" /> instances for request/response type pairs.
    /// </summary>
    public class JsonRpcHistoryFactory : IJsonRpcHistoryFactory
    {
        private static readonly object InboundRecordsLock = new();
        private static readonly Dictionary<string, InboundRecordRegistry> InboundRecords = new();

        /// <summary>
        ///     Creates a factory using the given <see cref="ICoreClient" /> module.
        /// </summary>
        /// <param name="coreClient">The Core client to use for history instances.</param>
        public JsonRpcHistoryFactory(ICoreClient coreClient)
        {
            CoreClient = coreClient;
        }

        /// <summary>
        ///     The Core client this factory serves.
        /// </summary>
        public ICoreClient CoreClient { get; }

        /// <summary>
        ///     Gets the initialized singleton history instance for the given request and response types.
        /// </summary>
        /// <typeparam name="T">The request type to store history for.</typeparam>
        /// <typeparam name="TR">The response type to store history for.</typeparam>
        /// <returns>The singleton history instance for the Core client context.</returns>
        public async Task<IJsonRpcHistory<T, TR>> JsonRpcHistoryOfType<T, TR>()
        {
            return (await JsonRpcHistoryHolder<T, TR>.InstanceForContext(CoreClient).ConfigureAwait(false)).History;
        }

        /// <summary>
        ///     Registers the concrete history that recorded an inbound request so a response published through a
        ///     different generic transport pair can remove that exact record.
        /// </summary>
        /// <param name="coreClient">The client that owns the record.</param>
        /// <param name="topic">The request topic.</param>
        /// <param name="id">The request id.</param>
        /// <param name="history">The history instance that stored the record.</param>
        internal static void RegisterInboundRecord(ICoreClient coreClient, string topic, long id,
            IJsonRpcHistoryInternal history)
        {
            lock (InboundRecordsLock)
            {
                if (!InboundRecords.TryGetValue(coreClient.Context, out var registry) ||
                    !ReferenceEquals(registry.CoreClient, coreClient))
                {
                    registry = new InboundRecordRegistry(coreClient);
                    InboundRecords[coreClient.Context] = registry;
                }

                registry.Records[new InboundRecordKey(topic, id)] = history;
            }
        }

        /// <summary>
        ///     Removes the inbound-record registration when its owning history removes the record by another path.
        /// </summary>
        /// <param name="coreClient">The client that owns the record.</param>
        /// <param name="topic">The request topic.</param>
        /// <param name="id">The request id.</param>
        /// <param name="history">The history instance that owned the record.</param>
        internal static void UnregisterInboundRecord(ICoreClient coreClient, string topic, long id,
            IJsonRpcHistoryInternal history)
        {
            lock (InboundRecordsLock)
            {
                if (!InboundRecords.TryGetValue(coreClient.Context, out var registry) ||
                    !ReferenceEquals(registry.CoreClient, coreClient))
                {
                    return;
                }

                var key = new InboundRecordKey(topic, id);
                if (registry.Records.TryGetValue(key, out var registered) && ReferenceEquals(registered, history))
                {
                    registry.Records.Remove(key);
                }

                if (registry.Records.Count == 0)
                {
                    InboundRecords.Remove(coreClient.Context);
                }
            }
        }

        /// <summary>
        ///     Removes the inbound history record associated with a response after that response was published.
        /// </summary>
        /// <param name="coreClient">The client publishing the response.</param>
        /// <param name="topic">The response topic.</param>
        /// <param name="id">The response id.</param>
        internal static void RemoveInboundRecord(ICoreClient coreClient, string topic, long id)
        {
            IJsonRpcHistoryInternal history = null;
            lock (InboundRecordsLock)
            {
                if (!InboundRecords.TryGetValue(coreClient.Context, out var registry) ||
                    !ReferenceEquals(registry.CoreClient, coreClient))
                {
                    return;
                }

                var key = new InboundRecordKey(topic, id);
                if (!registry.Records.TryGetValue(key, out history))
                {
                    return;
                }

                registry.Records.Remove(key);
                if (registry.Records.Count == 0)
                {
                    InboundRecords.Remove(coreClient.Context);
                }
            }

            history.TryDeleteByDirection(topic, id, JsonRpcRecordDirection.Inbound);
        }

        /// <summary>
        ///     Removes an outbound record from the concrete history instance that routed its response.
        /// </summary>
        /// <typeparam name="T">The request type of the history instance.</typeparam>
        /// <typeparam name="TR">The response type of the history instance.</typeparam>
        /// <param name="history">The history instance that dispatched the response.</param>
        /// <param name="topic">The response topic.</param>
        /// <param name="id">The response id.</param>
        internal static void RemoveOutboundRecord<T, TR>(IJsonRpcHistory<T, TR> history, string topic, long id)
        {
            if (history is IJsonRpcHistoryInternal internalHistory)
            {
                internalHistory.TryDeleteByDirection(topic, id, JsonRpcRecordDirection.Outbound);
            }
        }

        /// <summary>
        ///     Removes all inbound-record registrations owned by a disposed history instance.
        /// </summary>
        /// <param name="coreClient">The client that owns the history instance.</param>
        /// <param name="history">The disposed history instance.</param>
        internal static void UnregisterHistory(ICoreClient coreClient, IJsonRpcHistoryInternal history)
        {
            lock (InboundRecordsLock)
            {
                if (!InboundRecords.TryGetValue(coreClient.Context, out var registry) ||
                    !ReferenceEquals(registry.CoreClient, coreClient))
                {
                    return;
                }

                var keys = registry.Records.Where(pair => ReferenceEquals(pair.Value, history)).Select(pair => pair.Key)
                    .ToArray();
                foreach (var key in keys)
                {
                    registry.Records.Remove(key);
                }

                if (registry.Records.Count == 0)
                {
                    InboundRecords.Remove(coreClient.Context);
                }
            }
        }

        /// <summary>
        ///     Holds singleton instances by Core context for one closed request/response generic pair.
        /// </summary>
        /// <typeparam name="T">The request type to store history for.</typeparam>
        /// <typeparam name="TR">The response type to store history for.</typeparam>
        public class JsonRpcHistoryHolder<T, TR>
        {
            private static readonly object HistoryLock = new();
            private static readonly Dictionary<string, JsonRpcHistoryHolder<T, TR>> Instance = new();
            private readonly ICoreClient _coreClient;

            private JsonRpcHistoryHolder(ICoreClient coreClient)
            {
                _coreClient = coreClient;
                History = new JsonRpcHistory<T, TR>(coreClient);
            }

            /// <summary>
            ///     The history instance held for this context.
            /// </summary>
            public IJsonRpcHistory<T, TR> History { get; }

            /// <summary>
            ///     Gets a live singleton for a Core context. A cached holder whose history or owning client was
            ///     disposed is torn down and replaced before it can retain a stale heartbeat subscription.
            /// </summary>
            /// <param name="coreClient">The Core client to use for the context.</param>
            /// <returns>A live initialized holder for the context.</returns>
            public static async Task<JsonRpcHistoryHolder<T, TR>> InstanceForContext(ICoreClient coreClient)
            {
                JsonRpcHistoryHolder<T, TR> stale = null;
                JsonRpcHistoryHolder<T, TR> historyHolder;
                var context = coreClient.Context;

                lock (HistoryLock)
                {
                    if (Instance.TryGetValue(context, out var existing))
                    {
                        if (!existing._coreClient.Disposed && !((JsonRpcHistory<T, TR>)existing.History).IsDisposed)
                        {
                            historyHolder = existing;
                        }
                        else
                        {
                            stale = existing;
                            Instance.Remove(context);
                            historyHolder = new JsonRpcHistoryHolder<T, TR>(coreClient);
                            Instance.Add(context, historyHolder);
                        }
                    }
                    else
                    {
                        historyHolder = new JsonRpcHistoryHolder<T, TR>(coreClient);
                        Instance.Add(context, historyHolder);
                    }
                }

                if (stale != null)
                {
                    stale.History.Dispose();
                }

                try
                {
                    await historyHolder.History.Init().ConfigureAwait(false);
                    return historyHolder;
                }
                catch
                {
                    lock (HistoryLock)
                    {
                        if (Instance.TryGetValue(context, out var registered) && ReferenceEquals(registered, historyHolder))
                        {
                            Instance.Remove(context);
                        }
                    }

                    historyHolder.History.Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        ///     Stores inbound record ownership for one live Core client context.
        /// </summary>
        private sealed class InboundRecordRegistry
        {
            /// <summary>
            ///     Creates a registry for a Core client.
            /// </summary>
            /// <param name="coreClient">The client that owns all registered records.</param>
            public InboundRecordRegistry(ICoreClient coreClient)
            {
                CoreClient = coreClient;
            }

            /// <summary>
            ///     The client that owns this registry.
            /// </summary>
            public ICoreClient CoreClient { get; }

            /// <summary>
            ///     Maps an inbound topic/id identity to its concrete history instance.
            /// </summary>
            public Dictionary<InboundRecordKey, IJsonRpcHistoryInternal> Records { get; } = new();
        }

        /// <summary>
        ///     Identifies an inbound record independently of the generic transport pair used to publish its response.
        /// </summary>
        private readonly struct InboundRecordKey : IEquatable<InboundRecordKey>
        {
            /// <summary>
            ///     Creates an inbound record key.
            /// </summary>
            /// <param name="topic">The record topic.</param>
            /// <param name="id">The record id.</param>
            public InboundRecordKey(string topic, long id)
            {
                Topic = topic;
                Id = id;
            }

            private string Topic { get; }
            private long Id { get; }

            /// <summary>
            ///     Determines whether another key identifies the same inbound record.
            /// </summary>
            /// <param name="other">The key to compare.</param>
            /// <returns>True when the keys are equal; otherwise false.</returns>
            public bool Equals(InboundRecordKey other)
            {
                return Id == other.Id && string.Equals(Topic, other.Topic, StringComparison.Ordinal);
            }

            /// <summary>
            ///     Determines whether another object is an equal inbound record key.
            /// </summary>
            /// <param name="obj">The object to compare.</param>
            /// <returns>True when the object is an equal key; otherwise false.</returns>
            public override bool Equals(object obj)
            {
                return obj is InboundRecordKey other && Equals(other);
            }

            /// <summary>
            ///     Gets the hash code for this inbound record key.
            /// </summary>
            /// <returns>The hash code for this key.</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Topic == null ? 0 : StringComparer.Ordinal.GetHashCode(Topic)) * 397) ^ Id.GetHashCode();
                }
            }
        }
    }
}
