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
            
            Debug.Log($"[AppKit Init] Initializing AppKit...");
            await AppKit.InitializeAsync(
                new AppKitConfig(
                    projectId: "884a108399b5e7c9bc00bd9be4ccb2cc",
                    new Metadata( 
                        name: "AppKit Unity", 
                        description: "AppKit Unity Sample", 
                        url: "https://reown.com",
                        iconUrl: "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/appkit-icon.png",
                        new RedirectData
                        {
                            Native = "appkit-sample-unity"
                        }
                    )
                )
                {
                    customWallets = GetCustomWallets()
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
            return new[]
            {
                new Wallet
                {
                    Name = "Swift Wallet",
                    // ImageUrl = "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/flutter-icon.png",
                    MobileLink = "walletapp://"
                }
            };
        }
    }
}