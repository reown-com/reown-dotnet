using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.AppKit.Unity.Test;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Reown.AppKit.Unity.Tests
{
    internal class AppKitTestFixture
    {
        public DappTestView DappUi
        {
            get => _dappUi ??= new DappTestView(GameObject.Find("Dapp UI").GetComponent<UIDocument>().rootVisualElement);
        }

        public AppKitTestView AppKitUi
        {
            get => _appKitUi ??= new AppKitTestView(GameObject.Find("Reown AppKit").GetComponentInChildren<UIDocument>().rootVisualElement);
        }

        private DappTestView _dappUi;
        private AppKitTestView _appKitUi;


        [SetUp]
        public virtual void Setup()
        {
            PlayerPrefs.DeleteKey("WC_SELECTED_CHAIN_ID");
            SceneManager.LoadScene(0, LoadSceneMode.Single);
        }

        [UnityTearDown]
        public virtual IEnumerator TearDown()
        {
            return UniTask.ToCoroutine(async () =>
            {
                try
                {
                    if (AppKit.IsAccountConnected)
                        await AppKit.DisconnectAsync();

                    _dappUi = null;
                    _appKitUi = null;

                    await UnloadAllScenesAsync();

                    await UniTask.Delay(100);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            });
        }

        protected virtual async UniTask WaitForAppKitAsync()
        {
            if (!AppKit.IsInitialized)
            {
                var tcs = new UniTaskCompletionSource();
                AppKit.Initialized += (_, _) => tcs.TrySetResult();
                await tcs.Task;
            }

            await UniTask.Delay(100);
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
}