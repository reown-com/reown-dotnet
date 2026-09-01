using System;
using Newtonsoft.Json;
using Reown.Core.Crypto.Interfaces;
using Reown.Core.Interfaces;
using Reown.Core.Network;
using Reown.Core.Network.Interfaces;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;

namespace Reown.Core.Models
{
    /// <summary>
    ///     Options used to configure the Core module.
    /// </summary>
    /// <remarks>
    ///     <see cref="CoreClient" /> reads these options and never writes to them. Every module supplied here stays
    ///     owned by the caller: the client disposes only the modules it created itself, so a supplied storage,
    ///     keychain or crypto module is still usable after that client has been disposed.
    ///     One options object can therefore build one client after another, but only in sequence: two clients that
    ///     are live at the same time need their own storage and keychain, because both write the whole keychain to
    ///     the same storage key.
    /// </remarks>
    public class CoreOptions
    {
        /// <summary>
        ///     The Project ID to use to authenticate with the relay server
        /// </summary>
        [JsonProperty("projectId")] public string ProjectId { get; set; }

        /// <summary>
        ///     The name that this Core module will show itself as
        /// </summary>
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>
        ///     The URL of the relay server to connect to. This should not include any auth info
        /// </summary>
        [JsonProperty("relayUrl")] public string RelayUrl { get; set; }

        /// <summary>
        ///     The base context string to use for module isolation. If null or empty, then the string
        ///     "{Name}-client" will be used
        /// </summary>
        [JsonProperty("context")] public string BaseContext { get; set; }

        /// <summary>
        ///     The <see cref="IKeyValueStorage" /> module to use for storage. This module will be used by most Core modules
        ///     for storing data and by the default <see cref="IKeyChain" /> module (if no <see cref="IKeyChain" /> module is provided).
        ///     If this is set to null, then the default <see cref="FileSystemStorage" /> will be used, and that one is
        ///     disposed with the client that created it.
        ///     A storage set here belongs to the caller and is not disposed with the client, so it stays usable
        ///     afterwards. Do not dispose it while a client is still using it.
        /// </summary>
        [JsonProperty("storage")] public IKeyValueStorage Storage { get; set; }

        /// <summary>
        ///     The <see cref="IKeyChain" /> module to use for the <see cref="ICrypto" /> module.
        ///     If set to null, then the default <see cref="KeyChain" /> module will be used with the provided Storage
        ///     module, and that one is disposed with the client that created it.
        ///     A keychain set here belongs to the caller and is not disposed with the client, so it stays usable
        ///     afterwards. Two live clients must not share one keychain.
        /// </summary>
        [JsonProperty("keychain")] public IKeyChain KeyChain { get; set; }

        /// <summary>
        ///     The <see cref="IConnectionBuilder" /> interface to use inside the Relayer to build
        ///     new websocket connections.
        /// </summary>
        [JsonProperty("connectionBuilder")] public IConnectionBuilder ConnectionBuilder { get; set; }

        /// <summary>
        ///     The <see cref="ICrypto" /> module to use for crypto operations. This option
        ///     overrides the KeyChain option. If set to null, then a default Crypto module will be used
        ///     with either the KeyChain option or a default keychain, and that one is disposed with the client that
        ///     created it.
        ///     A crypto module set here belongs to the caller and is not disposed with the client.
        /// </summary>
        public ICrypto CryptoModule { get; set; }

        /// <summary>
        ///     How long the <see cref="IRelayer" /> should wait before throwing a <see cref="TimeoutException" /> duringn phase. If
        ///     this field is null, then the timeout will be infinite.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        ///     The <see cref="IRelayUrlBuilder" /> module to use for building the relay url.
        ///     If this is null, then the default <see cref="RelayUrlBuilder" /> module will be used by Core.
        /// </summary>
        public IRelayUrlBuilder RelayUrlBuilder { get; set; }
    }
}