using System.Threading.Tasks;
using Newtonsoft.Json;
using Reown.Core.Common.Logging;
using Reown.Core.Crypto;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;
using Reown.Sign.Models;
using UnityEngine;

namespace Reown.Sign.Unity
{
    public class SignClientUnity : SignClient
    {
        private bool _disposed;

        private SignClientUnity(SignClientOptions options) : base(options)
        {
            Linker = new Linker(this);
        }

        public Linker Linker { get; }

        public static async Task<SignClientUnity> Create(SignClientOptions options)
        {
            if (options.Storage == null)
            {
                var storage = await BuildUnityStorage();
                options.Storage = storage;
                options.KeyChain ??= new KeyChain(storage);
            }

            options.RelayUrlBuilder ??= new UnityRelayUrlBuilder();
            options.ConnectionBuilder ??= new ConnectionBuilderUnity();

            var sign = new SignClientUnity(options);
            await sign.Initialize();
            return sign;
        }

        private static async Task<IKeyValueStorage> BuildUnityStorage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var currentSyncContext = System.Threading.SynchronizationContext.Current;
            if (currentSyncContext.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
                throw new System.Exception(
                    $"[Reown.Sign.Unity] SynchronizationContext is not of type UnityEngine.UnitySynchronizationContext. Current type is <i>{currentSyncContext.GetType().FullName}</i>. When targeting WebGL, Make sure to initialize SignClient from the main thread.");

            var playerPrefsStorage = new PlayerPrefsStorage(currentSyncContext);
            await playerPrefsStorage.Init();

            return playerPrefsStorage;
#endif

            var path = $"{Application.persistentDataPath}/Reown/storage.json";
            ReownLogger.Log($"[Reown.Sign.Unity] Using storage path <i>{path}</i>");

            var storage = new FileSystemStorage(path);

            try
            {
                await storage.Init();
            }
            catch (JsonException)
            {
                Debug.LogError($"[Reown.Sign.Unity] Failed to deserialize storage. Deleting it and creating a new one at <i>{path}</i>");
                await storage.Clear();
                await storage.Init();
            }

            return storage;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                Linker.Dispose();
            }

            base.Dispose(disposing);
            _disposed = true;
        }
    }
}