using System;
using Reown.AppKit.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace WalletConnect.Web3Modal.CustomizationSample
{
    public class Dapp : MonoBehaviour
    {
        [SerializeField] private Button _connectButton;
        [SerializeField] private Button _accountButton;

        private void Awake()
        {
            Application.targetFrameRate = Screen.currentResolution.refreshRate;
            _connectButton.interactable = false;
            _accountButton.interactable = false;
        }

        private async void Start()
        {
            Debug.Log("Init AppKit...");
            try
            {
                await AppKit.InitializeAsync(
                    new AppKitConfig(
                        projectId: "8ae0986b46907f3df49bbc34e081f3c4",
                        new Metadata( 
                            name: "AppKit Customization", 
                            description: "AppKit Unity Customization Sample", 
                            url: "https://reown.com",
                            iconUrl: "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/appkit-icon.png"
                        )
                    )
                );

                // Use custom AccountView on desktop
                if (!Application.isMobilePlatform)
                {
                    if (AppKit.ModalController is ModalControllerUtk modalController)
                    {
                        var routerController = modalController.RouterController;
                        var customAccountPresenter = new CustomAccountPresenter(routerController, routerController.RootVisualElement);
                        routerController.RegisterModalView(ViewType.Account, customAccountPresenter);
                    }
                }

                AppKit.AccountConnected += async (_, e) =>
                {
                    _connectButton.interactable = false;
                    _accountButton.interactable = true;
                };

                AppKit.AccountDisconnected += (_, _) =>
                {
                    _connectButton.interactable = true;
                    _accountButton.interactable = false;
                };

                var resumed = await AppKit.ConnectorController.TryResumeSessionAsync();

                _connectButton.interactable = !resumed;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AppKit] Initialization failed: {e.Message}");
            }
        }

        public void OnConnectButton()
        {
            AppKit.OpenModal();
        }

        public void OnAccountButton()
        {
            AppKit.OpenModal(ViewType.Account);
        }
    }
}