using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Model;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Crypto.Encoder;
using Reown.Core.Network.Models;
using Reown.Sign.Unity;
using Solana.Unity.Wallet.Utilities;
using UnityEngine;
using UnityEngine.UI;

public class Main : MonoBehaviour
{
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _signButton;
    [SerializeField] private Button _accountButton;

    private void Awake()
    {
        _connectButton.onClick.AddListener(() => AppKit.OpenModal());
        _signButton.onClick.AddListener(async () => await SignMessage());
        _accountButton.onClick.AddListener(() => AppKit.OpenModal(ViewType.Account));
    }

    public async void Start()
    {
        // Set up Reown logger to collect logs from AppKit
        ReownLogger.Instance = new UnityLogger();
        
        // Create AppKit config object
        var config = new AppKitConfig
        {
            // Project ID from https://cloud.reown.com/
            projectId = "610570e13c15bf6e35d12aa2ba945100", // don't use this project id in prod!
            metadata = new Metadata(
                name: "Solana Core",
                description: "Testing Solana.Unity-Core + Reown AppKit",
                url: "https://reown.com",
                iconUrl: "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/develop/sample/Reown.AppKit.Unity/Assets/Textures/appkit-icon-unity.png",
                new RedirectData
                {
                    Native = "com.Reown.SolanaCore"
                }
            ),
            customWallets = new []
            {
                new Wallet
                {
                    Name = "Flutter Sample Wallet",
                    ImageUrl = "https://github.com/reown-com/reown-dotnet/blob/develop/media/wallet-flutter.png?raw=true",
                    MobileLink = "wcflutterwallet://"
                },
            },
            supportedChains = new[]
            {
                ChainConstants.Chains.Solana,
            }
        };
        // Initialize AppKit with config
        await AppKit.InitializeAsync(config);
        
        await ResumeSessionOrSubscribeToAccountConnectedEvent();
    }

    public async Task ResumeSessionOrSubscribeToAccountConnectedEvent()
    {
        // Try to resume account connection from the last session
        var resumed = await AppKit.ConnectorController.TryResumeSessionAsync();

        if (resumed)
        {
            // Continue to the game
            MyAccountConnectedHandler();
        }
        else
        {
            // Subscribe to account connected event
            AppKit.AccountConnected += (_, e) => MyAccountConnectedHandler();
        }
    }
    
    // Called when previous session resumed or when user connected account from AppKit modal UI
    private void MyAccountConnectedHandler()
    {
        Debug.Log("Account connected");
        
        _connectButton.interactable = false;
        _signButton.interactable = true;
        _accountButton.interactable = true;
        
        AppKit.AccountDisconnected += (_, e) => MyAccountDisconnectedHandler();

    }
    
    // Called when user disconnected
    private void MyAccountDisconnectedHandler()
    {
        Debug.Log("Account disconnected");
        
        _connectButton.interactable = true;
        _signButton.interactable = false;
        _accountButton.interactable = false;
    }

    private static async Task SignMessage()
    {
        Debug.Log("Signing message...");

        const string message = "Hello, Solana!";
        var sig = await AppKit.Solana.SignMessageAsync(message);
        Debug.Log($"Signature: {sig}");
        
        var isValid = await AppKit.Solana.VerifyMessageSignatureAsync(message, sig);
        Debug.Log($"Signature verified: {isValid}");
    }
}
