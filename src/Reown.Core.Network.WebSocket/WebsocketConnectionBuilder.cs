using System;
using System.Threading.Tasks;
using Reown.Core.Network.Interfaces;

namespace Reown.Core.Network.Websocket
{
    public class WebsocketConnectionBuilder : IConnectionBuilder
    {
        /// <summary>
        ///     Applied to every connection this builder creates. See
        ///     <see cref="WebsocketConnection.OpenTimeout" /> for why it is worth setting: it, not the
        ///     relayer's own timeout, is what paces reconnection attempts.
        /// </summary>
        public TimeSpan OpenTimeout { get; set; } = WebsocketConnection.DefaultOpenTimeout;

        /// <inheritdoc cref="WebsocketConnection.KeepAliveTimeout" />
        public TimeSpan? KeepAliveTimeout { get; set; }

        public Task<IJsonRpcConnection> CreateConnection(string url, string context = null)
        {
            WebsocketConnection connection = new WebsocketConnection(url, context)
            {
                OpenTimeout = OpenTimeout,
                KeepAliveTimeout = KeepAliveTimeout
            };

            return Task.FromResult<IJsonRpcConnection>(connection);
        }
    }
}
