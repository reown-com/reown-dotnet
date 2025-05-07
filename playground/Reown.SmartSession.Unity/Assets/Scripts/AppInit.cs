using System.Linq;
using IngameDebugConsole;
using Newtonsoft.Json;
using Reown.AppKit.Unity;
using Skibitsky.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sample
{
    public class AppInit : MonoBehaviour
    {
        [SerializeField] private SceneReference _mainScene;

        [Space]
        [SerializeField] private GameObject _debugConsole;

        private void Start()
        {
            InitDebugConsole();
            SceneManager.LoadScene(_mainScene);
        }

        private void InitDebugConsole()
        {
#if UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS
            Debug.developerConsoleVisible = false; // disable Unity console
            DontDestroyOnLoad(gameObject);
            if (_debugConsole != null)
                _debugConsole.SetActive(true);
#endif
        }

        [ConsoleMethod("accounts", "Lists all connected accounts")]
        public static async void Accounts()
        {
            var accounts = await AppKit.ConnectorController.GetAccountsAsync();

            if (accounts == null || accounts.Length == 0)
            {
                Debug.Log("No accounts connected");
                return;
            }

            foreach (var account in accounts)
            {
                Debug.Log(account.AccountId);
            }
        }

        [ConsoleMethod("sessionProps", "Prints session properties")]
        public static void SessionProps()
        {
            var walletConnect = AppKit.ConnectorController.ActiveConnector as WalletConnectConnector;
            var addressProvider = walletConnect.SignClient.AddressProvider;
            var sessionProperties = addressProvider.DefaultSession.SessionProperties;

            if (sessionProperties == null)
            {
                Debug.Log("No session properties found");
                return;
            }

            var json = JsonConvert.SerializeObject(sessionProperties, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            });
            Debug.Log(json);
        }

        [ConsoleMethod("session", "Prints active session")]
        public static void Session()
        {
            var walletConnect = AppKit.ConnectorController.ActiveConnector as WalletConnectConnector;
            var session = walletConnect.SignClient.AddressProvider.DefaultSession;

            if (session == null)
            {
                Debug.Log("No active session found");
                return;
            }

            var json = JsonConvert.SerializeObject(session, new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            });
            Debug.Log(json);
        }

        [ConsoleMethod("webwallet", "Set Web Wallet URL", "Web Wallet URL")]
        public static void SetWebWalletUrl(string url)
        {
            if (url is "null" or null)
            {
                PlayerPrefs.DeleteKey("RE_WEB_WALLET_URL");
                PlayerPrefs.Save();
                Debug.Log("Web Wallet URL removed");
                return;
            }

            PlayerPrefs.SetString("RE_WEB_WALLET_URL", url);
            PlayerPrefs.SetString("RE_RECENT_WALLET_DEEPLINK", url);
            PlayerPrefs.Save();
            Debug.Log($"Web Wallet URL set to {url}");
        }
    }
}