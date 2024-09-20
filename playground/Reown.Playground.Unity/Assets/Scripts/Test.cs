using Reown.AppKit.Unity;
using Reown.Core.Common.Logging;
using Reown.Sign.Unity;
using UnityEngine;
using Metadata = Reown.AppKit.Unity.Metadata;

namespace Playground.Unity
{
    public class Test : MonoBehaviour
    {
        private async void Start()
        {
            ReownLogger.Instance = new UnityLogger();

            await AppKit.InitializeAsync(
                new AppKitConfig("8ae0986b46907f3df49bbc34e081f3c4",
                    new Metadata("test", "test", "https://walletconnect.com",
                        "https://raw.githubusercontent.com/WalletConnect/WalletConnectUnity/project/modal-sample/.github/media/unity-logo.png")
                )
            );

            // AppKit.OpenModal();


            // Debug.Log("[TEST] Init");
            // var sign = await SignClientUnity.Create(new SignClientOptions
            // {
            //     Name = "test",
            //     ProjectId = "8ae0986b46907f3df49bbc34e081f3c4",
            //     Metadata = new Metadata
            //     {
            //         Name = "test",
            //         Description = "test",
            //         Url = "https://walletconnect.com",
            //         Icons = new[]
            //         {
            //             "https://raw.githubusercontent.com/WalletConnect/WalletConnectUnity/project/modal-sample/.github/media/unity-logo.png"
            //         }
            //     }
            // });
            //
            // Debug.Log("[TEST] Init Done");
            //
            // var dappConnectOptions = new ConnectOptions
            // {
            //     RequiredNamespaces = new RequiredNamespaces
            //     {
            //         {
            //             "eip155", new ProposedNamespace
            //             {
            //                 Methods = new[]
            //                 {
            //                     "eth_sendTransaction",
            //                     "eth_signTransaction",
            //                     "eth_sign",
            //                     "personal_sign",
            //                     "eth_signTypedData"
            //                 },
            //                 Chains = new[]
            //                 {
            //                     "eip155:1",
            //                     "eip155:10"
            //                 },
            //                 Events = new[]
            //                 {
            //                     "chainChanged",
            //                     "accountsChanged"
            //                 }
            //             }
            //         }
            //     }
            // };
            //
            // var connectData = await sign.Connect(dappConnectOptions);
            // Debug.Log($"[TEST] Connect: {connectData.Uri}");
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
            await AppKit.Evm.SignMessageAsync("Labas!");
        }
    }
}