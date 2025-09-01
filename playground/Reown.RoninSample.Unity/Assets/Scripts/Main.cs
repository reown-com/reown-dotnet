using System.Threading.Tasks;
using Reown.AppKit.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
///     Sample Unity <see cref="MonoBehaviour"/> demonstrating how to use the Reown AppKit SDK
///     with the Ronin chain and Ronin Wallet.
/// </summary>
/// <remarks>
///     After <c>Start</c>, the app will try to resume the previous session or subscribe to the
///     account-connected event. Users can connect their wallets by clicking the "Connect" button,
///     which opens the AppKit modal UI. After connecting, users can sign a message and verify it
///     with the connected wallet.
/// </remarks>
/// <example>
///     Alternatively, on mobile native platforms, users can connect directly to the Ronin wallet:
///     <code language="csharp">
///         await AppKit.ConnectAsync("541d5dcd4ede02f3afaf75bf8e3e4c4f1fb09edb5fa6c4377ebf31c2785d9adf");
///     </code>
///     where the parameter is the Ronin Wallet ID (see https://walletguide.walletconnect.network).
/// </example>
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
        // Create AppKit config object
        var config = new AppKitConfig
        {
            // Project ID from https://cloud.reown.com/
            projectId = "610570e13c15bf6e35d12aa2ba945100", // don't use this project id in prod!
            metadata = new Metadata(
                name: "Ronin Sample",
                description: "Sample Unity project demonstrating how to use Reown AppKit SDK with Ronin chain and Ronin Wallet.",
                url: "https://reown.com",
                iconUrl: "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/develop/sample/Reown.AppKit.Unity/Assets/Textures/appkit-icon-unity.png"
            ),
            // Optional. Can be used to show only specific wallets in AppKit UI
            // Wallet IDs can be found at: https://walletguide.walletconnect.network
            includedWalletIds = new[]
            {
                // Ronin Wallet
                "541d5dcd4ede02f3afaf75bf8e3e4c4f1fb09edb5fa6c4377ebf31c2785d9adf",
                // MetaMask
                "c57ca95b47569778a828d19178114f4db188b89b763c899ba0be274e97267d96",
                // Trust
                "4622a2b2d6af1c9844944291e5e7351a6aa24cd7b23099efac1b2fd875da31a0"
            },
            supportedChains = new[]
            {
                ChainConstants.Chains.Ronin,
                ChainConstants.Chains.RoninSaigon,
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

        const string message = "Hello, Ronin!";
        
        // Sign a message with connected wallet.
        // Wallet returns a message signature that we can use to verify user's address
        var messageSignature = await AppKit.Evm.SignMessageAsync(message);

        var connectedAccount = await AppKit.GetAccountAsync();
        
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
