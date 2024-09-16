using System;
using System.Threading.Tasks;
using Reown.Core.Controllers;
using Reown.Core.Crypto;
using Reown.Core.Crypto.Interfaces;
using Reown.Core.Interfaces;
using Reown.Core.Models;
using Reown.Core.Models.Relay;
using Reown.Core.Models.Verify;
using Reown.Core.Network;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;

namespace Reown.Core
{
    /// <summary>
    ///     This module holds all Core Modules and holds configuration data
    ///     required by several Core Module.
    /// </summary>
    public class CoreClient : ICoreClient
    {
        /// <summary>
        ///     The prefix string used for the storage key
        /// </summary>
        public static readonly string StoragePrefix = ICoreClient.Protocol + "@" + ICoreClient.Version + ":core:";

        private readonly string _optName;
        private readonly string guid = "";

        /// <summary>
        ///     Create a new Core with the given options.
        /// </summary>
        /// <param name="options">The options to use to configure the new Core module</param>
        public CoreClient(CoreOptions options = null)
        {
            if (options == null)
            {
                var storage = new InMemoryStorage();
                options = new CoreOptions
                {
                    KeyChain = new KeyChain(storage),
                    ProjectId = null,
                    RelayUrl = null,
                    Storage = storage
                };
            }

            if (options.Storage == null)
            {
                options.Storage = new FileSystemStorage();
            }
            
            options.RelayUrlBuilder ??= new RelayUrlBuilder();

            Options = options;
            ProjectId = options.ProjectId;
            RelayUrl = options.RelayUrl;
            Storage = options.Storage;

            if (options.CryptoModule != null)
            {
                Crypto = options.CryptoModule;
            }
            else
            {
                if (options.KeyChain == null)
                {
                    options.KeyChain = new KeyChain(options.Storage);
                }

                Crypto = new Crypto.Crypto(options.KeyChain);
            }

            HeartBeat = new HeartBeat();
            _optName = options.Name;

            Expirer = new Expirer(this);
            Pairing = new Pairing(this);
            Verify = new Verifier();

            Relayer = new Relayer(new RelayerOptions
            {
                CoreClient = this,
                ProjectId = ProjectId,
                RelayUrl = options.RelayUrl,
                ConnectionTimeout = options.ConnectionTimeout,
                RelayUrlBuilder = options.RelayUrlBuilder
            });

            MessageHandler = new TypedMessageHandler(this);
            History = new JsonRpcHistoryFactory(this);
        }

        /// <summary>
        ///     If this module is initialized or not
        /// </summary>
        public bool Initialized { get; private set; }

        public bool Disposed { get; protected set; }

        public CoreOptions Options { get; }

        /// <summary>
        ///     The name of this module.
        /// </summary>
        public string Name
        {
            get => $"{_optName}-core";
        }

        /// <summary>
        ///     The current context of this module instance.
        /// </summary>
        public string Context
        {
            get => $"{Name}{guid}";
        }

        /// <summary>
        ///     The <see cref="IHeartBeat" /> module this Core module is using
        /// </summary>
        public IHeartBeat HeartBeat { get; }

        /// <summary>
        ///     The <see cref="IJsonRpcHistoryFactory" /> factory this Sign Client module is using. Used for storing
        ///     JSON RPC request and responses of various types T, TR
        /// </summary>
        public IJsonRpcHistoryFactory History { get; }

        public Verifier Verify { get; }

        /// <summary>
        ///     The url of the relay server to connect to in the <see cref="IRelayer" /> module
        /// </summary>
        public string RelayUrl { get; }

        /// <summary>
        ///     The Project ID to use for authentication on the relay server
        /// </summary>
        public string ProjectId { get; }

        /// <summary>
        ///     The <see cref="ICrypto" /> module this Core module is using
        /// </summary>
        public ICrypto Crypto { get; }

        /// <summary>
        ///     The <see cref="IRelayer" /> module this Core module is using
        /// </summary>
        public IRelayer Relayer { get; }

        /// <summary>
        ///     The <see cref="IKeyValueStorage" /> module this Core module is using. All
        ///     Core Modules should use this for storage.
        /// </summary>
        public IKeyValueStorage Storage { get; }

        /// <summary>
        ///     The <see cref="ITypedMessageHandler" /> module this Core module is using. Use this for handling
        ///     custom message types (request or response) and for sending messages (request, responses or errors)
        /// </summary>
        public ITypedMessageHandler MessageHandler { get; }

        /// <summary>
        ///     The <see cref="IExpirer" /> module this Sign Client is using to track expiration dates
        /// </summary>
        public IExpirer Expirer { get; }

        /// <summary>
        ///     The <see cref="IPairing" /> module this Core module is using. Used for pairing two peers
        ///     with each other and keeping track of pairing state
        /// </summary>
        public IPairing Pairing { get; }

        /// <summary>
        ///     Start this module, this will initialize all Core Modules. If this module has already been
        ///     initialized, then nothing will happen
        /// </summary>
        public async Task Start()
        {
            if (Initialized) return;

            Initialized = true;
            await Initialize();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private async Task Initialize()
        {
            await Storage.Init();
            await Crypto.Init();
            await Relayer.Init();
            await HeartBeat.InitAsync();
            await Expirer.Init();
            await MessageHandler.Init();
            await Pairing.Init();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;

            if (disposing)
            {
                HeartBeat?.Dispose();
                Crypto?.Dispose();
                Relayer?.Dispose();
                Storage?.Dispose();
                MessageHandler?.Dispose();
                Expirer?.Dispose();
                Pairing?.Dispose();
                Verify?.Dispose();
            }

            Disposed = true;
        }
    }
}