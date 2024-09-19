using mixpanel;
using Reown.AppKit.Unity;
using Skibitsky.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sample
{
    public class AppKitInit : MonoBehaviour
    {
        [SerializeField] private SceneReference _menuScene;

        private async void Start()
        {
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
            );

            var clientId = await AppKit.Instance.SignClient.CoreClient.Crypto.GetClientId();
            Mixpanel.Identify(clientId);

            Debug.Log($"[AppKit Init] AppKit initialized. Loading menu scene...");
            SceneManager.LoadScene(_menuScene);
        }
    }
}