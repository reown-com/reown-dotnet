using System;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using UnityEngine;
using UnityEngine.UI;

public class Main : MonoBehaviour
{
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _signButton;
    [SerializeField] private Button _accountButton;

    private void Awake()
    {
        _connectButton.onClick.AddListener(async () => await ConnectAsync());
        _signButton.onClick.AddListener(async () => await SignMessageAsync());
        _accountButton.onClick.AddListener(() => AppKit.OpenModal(ViewType.Account));
    }

    public async void Start()
    {
        // Create AppKit config object
        var config = new AppKitConfig
        {
            // Project ID from https://cloud.reown.com/
            projectId = "610570e13c15bf6e35d12aa2ba945100", // don't use this project id in prod!
            metadata = new Metadata(
                name: "My Unity Game",
                description: "Short description",
                url: "https://example.com",
                iconUrl: "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/appkit-icon.png"
            ),
            // Optional. Can be used to show only specific wallets in AppKit UI
            // Wallet IDs can be found at: https://walletguide.walletconnect.network
            includedWalletIds = new[]
            {
                // Abstract Global Wallet
                "26d3d9e7224a1eb49089aa5f03fb9f3b883e04050404594d980d4e1e74e1dbea",
                // MetaMask
                "c57ca95b47569778a828d19178114f4db188b89b763c899ba0be274e97267d96"
            },
            supportedChains = new[]
            {
                ChainConstants.Chains.Abstract,
                ChainConstants.Chains.AbstractTestnet,
            }
        };
        
        // Initialize AppKit with config
        await AppKit.InitializeAsync(config);
        
        Debug.Log("App Initialized");
        
        await TryResumeSessionAsync();
    }
    
    private async Task TryResumeSessionAsync()
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
            MyAccountDisconnectedHandler();
        }
    }

    private async Task ConnectAsync()
    {
        // Directly connect to Abstract Global Wallet
        // Wallet ID is from http://walletguide.walletconnect.network
        await AppKit.ConnectAsync("26d3d9e7224a1eb49089aa5f03fb9f3b883e04050404594d980d4e1e74e1dbea");

        MyAccountConnectedHandler();
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

    private static async Task SignMessageAsync()
    {
        Debug.Log("Signing message...");

        const string message = "Hello, Abstract!";
        
        // Sign a message with connected wallet.
        // Wallet returns a message signature that we can use to verify user's address
        var messageSignature = await AppKit.Evm.SignMessageAsync(message);

        var connectedAccount = AppKit.Account;
        
        var verifyMessageParams = new VerifyMessageSignatureParams
        {
            Address = connectedAccount.Address,
            Message = message,
            Signature = messageSignature
        };
        var isSignatureValid = await AppKit.Evm.VerifyMessageSignatureAsync(verifyMessageParams);
        
        if (isSignatureValid)
        {
            Debug.Log($"Message signature is valid.");
        }
        else
        {
            Debug.LogError($"Message signature is invalid.");
        }
    }
}
