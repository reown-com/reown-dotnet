using System.Collections.Generic;
using System.Threading.Tasks;
using Reown.Core.Interfaces;

namespace Reown.Core.Controllers
{
    /// <summary>
    ///     A factory that creates/tracks instances of <see cref="JsonRpcHistory{T,TR}" /> for the given
    ///     types T and TR. This is used to track the history of requests and responses for a given session. This factory
    ///     is NOT a singleton, rather each ICore instance should have its own instance of this factory. The
    ///     <see cref="JsonRpcHistory{T,TR}" /> instance the factory returns must act as a singleton for the given
    ///     ICore context. The factory implementation must be context-aware, meaning that it must be able to separate different
    ///     singleton instances of <see cref="JsonRpcHistory{T,TR}" /> for different ICore instances.
    /// </summary>
    public class JsonRpcHistoryFactory : IJsonRpcHistoryFactory
    {
        /// <summary>
        ///     Create a new factory using the given <see cref="ICoreClient" /> module
        /// </summary>
        /// <param name="coreClient">The <see cref="ICoreClient" /> module to use for the factory</param>
        public JsonRpcHistoryFactory(ICoreClient coreClient)
        {
            CoreClient = coreClient;
        }

        /// <summary>
        ///     The <see cref="ICoreClient" /> instance this factory is for
        /// </summary>
        public ICoreClient CoreClient { get; }

        /// <summary>
        ///     Get the <see cref="IJsonRpcHistory{T,TR}" /> singleton instance with the given types
        ///     T, TR using the <see cref="ICoreClient" /> instance context string
        /// </summary>
        /// <typeparam name="T">The request type to store history for</typeparam>
        /// <typeparam name="TR">The response type to store history for</typeparam>
        /// <returns>The <see cref="IJsonRpcHistory{T,TR}" /> singleton instance with the given types T, TR</returns>
        public async Task<IJsonRpcHistory<T, TR>> JsonRpcHistoryOfType<T, TR>()
        {
            return (await JsonRpcHistoryHolder<T, TR>.InstanceForContext(CoreClient)).History;
        }

        /// <summary>
        ///     A holder class that holds different singletons by a context string for the specific
        ///     types T, TR
        ///     This is needed so that JsonRpcHistoryFactory does NOT need to be a generic class and can
        ///     instead use a generic method. Each singleton instance of this holder class
        ///     holds a single instance of <see cref="JsonRpcHistory{T,TR}" />
        /// </summary>
        /// <typeparam name="T">The request type to store history for</typeparam>
        /// <typeparam name="TR">The response type to store history for</typeparam>
        public class JsonRpcHistoryHolder<T, TR>
        {
            private static readonly object HistoryLock = new();
            private static readonly Dictionary<string, JsonRpcHistoryHolder<T, TR>> Instance = new();

            private JsonRpcHistoryHolder(ICoreClient coreClient)
            {
                History = new JsonRpcHistory<T, TR>(coreClient);
            }

            /// <summary>
            ///     The <see cref="IJsonRpcHistory{T,TR}" /> instance
            ///     this singleton is holding
            /// </summary>
            public IJsonRpcHistory<T, TR> History { get; }

            /// <summary>
            ///     Get the singleton instance for a specific ICore context. If no singleton already
            ///     exists, then a new instance will be created and stored.
            /// </summary>
            /// <param name="coreClient">The ICoe module to use the context string from</param>
            /// <returns>The singleton instance for the given ICore context</returns>
            public static async Task<JsonRpcHistoryHolder<T, TR>> InstanceForContext(ICoreClient coreClient)
            {
                JsonRpcHistoryHolder<T, TR> historyHolder;
                lock (HistoryLock)
                {
                    if (Instance.TryGetValue(coreClient.Context, out var context))
                        return context;

                    historyHolder = new JsonRpcHistoryHolder<T, TR>(coreClient);
                    Instance.Add(coreClient.Context, historyHolder);
                }

                await historyHolder.History.Init();
                return historyHolder;
            }
        }
    }
}