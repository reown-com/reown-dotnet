using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.Core;
using Reown.Core.Models;
using Reown.Core.Storage;
using Reown.Sign.Unity;
using Reown.WalletKit;
using Reown.WalletKit.Interfaces;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Reown.AppKit.Unity.Tests
{
    internal class AppKitTestFixture
    {
        public UILayer DappUi
        {
            get => _dappUi.Value;
        }

        public UILayer AppKitUi
        {
            get => _appKitUi.Value;
        }

        private readonly Lazy<UILayer> _dappUi = new(() =>
        {
            var uiDocument = GameObject.Find("Dapp UI").GetComponent<UIDocument>();
            return new UILayer(uiDocument);
        });

        private readonly Lazy<UILayer> _appKitUi = new(() =>
        {
            var uiDocument = GameObject.Find("Reown AppKit").GetComponentInChildren<UIDocument>();
            return new UILayer(uiDocument);
        });

        private readonly List<IWalletKit> _walletKits = new();

        [SetUp]
        public virtual void Setup()
        {
            SceneManager.LoadScene(0, LoadSceneMode.Single);
        }

        [UnityTearDown]
        public virtual IEnumerator TearDown()
        {
            return UniTask.ToCoroutine(async () =>
            {
                try
                {
                    foreach (var walletKit in _walletKits)
                        walletKit.Dispose();
                    _walletKits.Clear();

                    if (AppKit.IsAccountConnected)
                        await AppKit.DisconnectAsync();

                    await UnloadAllScenesAsync();
                    await UniTask.Delay(100);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            });
        }

        protected virtual async Task<IWalletKit> CreateWalletKitInstance()
        {
            var coreClient = new CoreClient(new CoreOptions
            {
                ConnectionBuilder = new ConnectionBuilderUnity(),
                ProjectId = "ef21cf313a63dbf63f2e9e04f3614029",
                Name = $"wallet-unity-e2e-test-{Guid.NewGuid().ToString()}",
                Storage = new InMemoryStorage()
            });

            var metadata = new Metadata("WalletKit", "Unity E2E Test WalletKit instance", "https://reown.com", "https://reown.com/favicon.ico");

            var wallet = await WalletKitClient.Init(coreClient, metadata);
            _walletKits.Add(wallet);

            return wallet;
        }

        protected virtual async UniTask UnloadAllScenesAsync(CancellationToken cancellationToken = default)
        {
            var emptyScene = SceneManager.CreateScene("Empty");
            SceneManager.SetActiveScene(emptyScene);

            var sceneCount = SceneManager.sceneCount;
            var unloadTasks = new UniTask[sceneCount];
            for (var i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.name == "Empty")
                    continue;

                unloadTasks[i] = SceneManager
                    .UnloadSceneAsync(scene, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects)
                    .ToUniTask(cancellationToken: cancellationToken);
            }
            await UniTask.WhenAll(unloadTasks);

            DestroyUndestroyableObjects();
        }

        // Destroys object currently living in the hidden DontDestroyOnLoad scene
        protected virtual void DestroyUndestroyableObjects()
        {
            // Create a throw-away object and flag it so Unity sticks it in the DDOL scene.
            var probe = new GameObject("[Reown] DDOL Probe");
            Object.DontDestroyOnLoad(probe);

            // Grab the hidden scene that the probe now lives in.
            var ddolScene = probe.scene;

            // Destroy every root object in that scene
            foreach (var root in ddolScene.GetRootGameObjects())
                if (root != null)
                    Object.Destroy(root);
        }
    }

    internal class UILayer
    {
        public UIDocument UIDocument { get; }

        public UILayer(UIDocument uiDocument)
        {
            UIDocument = uiDocument;
        }

        public T Q<T>(string name = null) where T : VisualElement
        {
            return UIDocument.rootVisualElement.Q<T>(name);
        }
    }
}