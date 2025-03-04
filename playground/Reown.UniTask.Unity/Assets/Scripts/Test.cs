using Cysharp.Threading.Tasks;
using Reown.AppKit.Unity;
using Reown.Core.Common.Logging;
using Reown.Sign.Unity;
using UnityEngine;
using Metadata = Reown.AppKit.Unity.Metadata;

namespace Playground.Unity
{
    public class Test : MonoBehaviour
    {
        private async UniTaskVoid Start()
        {
            Debug.Log($"Starting");

            Debug.Log("Wait for 2 seconds with UniTask.WaitForSeconds");
            await UniTask.WaitForSeconds(2f);

            Debug.Log("2 seconds passed");
            
            ReownLogger.Instance = new UnityLogger();


            Debug.Log("Initializing AppKit...");
            await AppKit.InitializeAsync(
                new AppKitConfig("95c7fd2ef1b28bdc326aa6a640e2422e",
                    new Metadata("test", "test", "https://walletconnect.com",
                        "https://raw.githubusercontent.com/WalletConnect/WalletConnectUnity/project/modal-sample/.github/media/unity-logo.png")
                )
            );
            
            Debug.Log("AppKit initialized");
        }


        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.M))
            {
                OpenModal();
            }
            else if (Input.GetKeyDown(KeyCode.A))
            {
                OpenAccount();
            }
        }

        public void OpenAccount()
        {
            AppKit.OpenModal(ViewType.Account);
        }

        public void OpenModal()
        {
            AppKit.OpenModal();
        }

        public async void Sign()
        {
            const string message = "Hello, world!";
            var signature = await AppKit.Evm.SignMessageAsync(message);
            Debug.Log($"Signature: {signature}");
        }
        
        public async UniTask<string> SignMessageAsync(string message)
        {
            return await AppKit.Evm.SignMessageAsync(message);
        }
    }
}