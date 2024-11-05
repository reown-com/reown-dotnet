using mixpanel;
using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Model;
using Reown.Core.Common.Logging;
using Skibitsky.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityLogger = Reown.Sign.Unity.UnityLogger;

namespace Sample
{
    public class AppKitInit : MonoBehaviour
    {
        [SerializeField] private SceneReference _menuScene;

        private async void Start()
        {
            ReownLogger.Instance = new UnityLogger();

            var siweConfig = new SiweConfig
            {
                GetMessageParams = () => new SiweMessageParams
                {
                    Domain = "my-domain",
                    Uri = "my-uri"
                }
            };
            
            Debug.Log($"[AppKit Init] Initializing AppKit...");
            await AppKit.InitializeAsync(
                new AppKitConfig
                {
                    projectId = "884a108399b5e7c9bc00bd9be4ccb2cc",
                    metadata = new Metadata(
                        "AppKit Unity",
                        "AppKit Unity Sample",
                        "https://reown.com",
                        "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/appkit-icon.png",
                        new RedirectData
                        {
                            Native = "appkit-sample-unity://"
                        }
                    ),
                    customWallets = GetCustomWallets(),
                    connectViewWalletsCountMobile = 5,
                    siweConfig = siweConfig
                }
            );
            

#if !UNITY_WEBGL
            var clientId = await AppKit.Instance.SignClient.CoreClient.Crypto.GetClientId();
            Mixpanel.Identify(clientId);
#endif

            Debug.Log($"[AppKit Init] AppKit initialized. Loading menu scene...");
            SceneManager.LoadScene(_menuScene);
        }

        private Wallet[] GetCustomWallets()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new[]
            {
                new Wallet
                {
                    Name = "Swift Wallet",
                    ImageUrl = "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/main/media/walletkit-icon.png",
                    MobileLink = "walletapp://"
                },
                new Wallet
                {
                    Name = "React Native Wallet",
                    ImageUrl = "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/main/media/walletkit-icon.png",
                    MobileLink = "rn-web3wallet://"
                },
                new Wallet
                {
                    Name = "Flutter Wallet Prod",
                    ImageUrl = "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/main/media/walletkit-icon.png",
                    MobileLink = "wcflutterwallet://"
                }
            };
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
            return new[]
            {
                new Wallet
                {
                    Name = "Kotlin Wallet",
                    ImageUrl = "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/main/media/walletkit-icon.png",
                    MobileLink = "kotlin-web3wallet://"
                },
                new Wallet
                {
                    Name = "React Native Wallet",
                    ImageUrl = "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/main/media/walletkit-icon.png",
                    MobileLink = "rn-web3wallet://"
                },
                new Wallet
                {
                    Name = "Flutter Wallet Prod",
                    ImageUrl = "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/main/media/walletkit-icon.png",
                    MobileLink = "wcflutterwallet://"
                }
            };
#endif
            return null;
        }
    }
}