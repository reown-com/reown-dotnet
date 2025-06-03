using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Model;
using Reown.Core.Common.Logging;
using Skibitsky.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityLogger = Reown.Sign.Unity.UnityLogger;

#if !UNITY_WEBGL
using mixpanel;
using Sentry;
#endif

namespace Sample
{
    public class AppKitInit : MonoBehaviour
    {
        [SerializeField] private SceneReference _menuScene;

        private async void Start()
        {
            // Set up Reown logger to collect logs from AppKit
            ReownLogger.Instance = new UnityLogger();

            // The very basic configuration of SIWE
            // Uncomment it and pass into AppKitConfig below to enable 1-Click Auth and SIWE
            // var siweConfig = new SiweConfig
            // {
            //     GetMessageParams = () => new SiweMessageParams
            //     {
            //         Domain = "example.com",
            //         Uri = "https://example.com/login"
            //     },
            //     SignOutOnChainChange = false
            // };
            //
            // // Subscribe to SIWE events
            // siweConfig.SignInSuccess += _ => Debug.Log("[Dapp] SIWE Sign In Success!");
            // siweConfig.SignOutSuccess += () => Debug.Log("[Dapp] SIWE Sign Out Success!");


            // AppKit configuration
            var appKitConfig = new AppKitConfig
            {
                // Project ID from https://cloud.reown.com/
                projectId = "884a108399b5e7c9bc00bd9be4ccb2cc",
                // siweConfig = siweConfig,
                metadata = new Metadata(
                    "AppKit Unity",
                    "AppKit Unity Sample",
                    "https://reown.com",
                    "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/appkit-icon.png",
                    new RedirectData
                    {
                        // Used by native wallets to redirect back to the app after approving requests
                        Native = "appkit-sample-unity://"
                    }
                ),
                customWallets = GetCustomWallets(),
                // On mobile show 5 wallets on the Connect view (the first AppKit modal screen)
                connectViewWalletsCountMobile = 5,
                supportedChains = new[]
                {
                    ChainConstants.Chains.Ethereum,
                    ChainConstants.Chains.Optimism,
                    ChainConstants.Chains.Arbitrum,
                    ChainConstants.Chains.Ronin,
                    ChainConstants.Chains.Avalanche,
                    ChainConstants.Chains.Base,
                    ChainConstants.Chains.Polygon
                },
                socials = new[]
                {
                    SocialLogin.Google,
                    SocialLogin.X,
                    SocialLogin.Discord,
                    SocialLogin.Apple,
                    SocialLogin.GitHub
                }
            };

            Debug.Log("[AppKit Init] Initializing AppKit...");

            await AppKit.InitializeAsync(
                appKitConfig
            );

#if !UNITY_WEBGL
            // The Mixpanel are Sentry are used by the sample project to collect telemetry
            var clientId = await AppKit.Instance.SignClient.CoreClient.Crypto.GetClientId();
            Mixpanel.Identify(clientId);

            SentrySdk.ConfigureScope(scope =>
            {
                scope.User = new SentryUser
                {
                    Id = clientId
                };
            });
#endif

            Debug.Log("[AppKit Init] AppKit initialized. Loading menu scene...");
            SceneManager.LoadScene(_menuScene);
        }

        /// <summary>
        ///     This method returns a list of Reown sample wallets on iOS and Android.
        ///     These wallets are used for testing and are not included in the default list of wallets returned by AppKit's REST
        ///     API.
        ///     On other platforms, this method returns null, so only the default list of wallets is used.
        /// </summary>
        private Wallet[] GetCustomWallets()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new[]
            {
                new Wallet
                {
                    Name = "Swift Wallet",
                    ImageUrl = "https://github.com/reown-com/reown-dotnet/blob/develop/media/wallet-swift.png?raw=true",
                    MobileLink = "walletapp://"
                },
                new Wallet
                {
                    Name = "React Native Wallet",
                    ImageUrl = "https://github.com/reown-com/reown-dotnet/blob/develop/media/wallet-rn.png?raw=true",
                    MobileLink = "rn-web3wallet://"
                },
                new Wallet
                {
                    Name = "Flutter Wallet Prod",
                    ImageUrl = "https://github.com/reown-com/reown-dotnet/blob/develop/media/wallet-flutter.png?raw=true",
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
                    ImageUrl = "https://github.com/reown-com/reown-dotnet/blob/develop/media/wallet-kotlin.png?raw=true",
                    MobileLink = "kotlin-web3wallet://"
                },
                new Wallet
                {
                    Name = "React Native Wallet",
                    ImageUrl = "https://github.com/reown-com/reown-dotnet/blob/develop/media/wallet-rn.png?raw=true",
                    MobileLink = "rn-web3wallet://"
                },
                new Wallet
                {
                    Name = "Flutter Wallet Prod",
                    ImageUrl = "https://github.com/reown-com/reown-dotnet/blob/develop/media/wallet-flutter.png?raw=true",
                    MobileLink = "wcflutterwallet://"
                }
            };
#endif
            return null;
        }
    }
}