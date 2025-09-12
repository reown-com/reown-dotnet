using System.Threading.Tasks;
using Reown.AppKit.Unity;
using Reown.Core.Common.Logging;
using Reown.Sign.Unity;
using UnityEngine;
using UnityEngine.UI;

public class Main : MonoBehaviour
{
    [SerializeField] private Button _connectButton;
    [SerializeField] private Button _signButton;
    [SerializeField] private Button _accountButton;
    [SerializeField] private Button _printBalanceButton;

    private void Awake()
    {
        _connectButton.onClick.AddListener(() => AppKit.OpenModal());
        _signButton.onClick.AddListener(async () => await SignMessage());
        _accountButton.onClick.AddListener(() => AppKit.OpenModal(ViewType.Account));
        _printBalanceButton.onClick.AddListener(async () => await PrintSolBalance());
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
                "Solana Core",
                "Testing Solana.Unity-Core + Reown AppKit",
                "https://reown.com",
                "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/develop/sample/Reown.AppKit.Unity/Assets/Textures/appkit-icon-unity.png",
                new RedirectData
                {
                    Native = "solanatest://"
                }
            ),
            supportedChains = new[]
            {
                ChainConstants.Chains.Solana
            }
        };
        // Initialize AppKit with config
        await AppKit.InitializeAsync(config);

        // Enable all buttons
        _connectButton.interactable = true;
        _signButton.interactable = false;
        _accountButton.interactable = false;
        _printBalanceButton.interactable = false;

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
        _printBalanceButton.interactable = true;

        AppKit.AccountDisconnected += (_, e) => MyAccountDisconnectedHandler();
    }

    // Called when user disconnected
    private void MyAccountDisconnectedHandler()
    {
        Debug.Log("Account disconnected");

        _connectButton.interactable = true;
        _signButton.interactable = false;
        _accountButton.interactable = false;
        _printBalanceButton.interactable = false;
    }

    private static async Task PrintSolBalance()
    {
        // Cached SOL balance from Reown's Blockchain API
        // It's a float, so it's not precise, but good enough for UI
        // The balance is updated when an account is connected and when user changes active chain
        // You can also manually update the balance by calling AppKit.AccountController.UpdateBalance()
        var formattedTokenBalance = AppKit.AccountController.NativeTokenBalance;
        Debug.Log($"Formatted SOL balance: {formattedTokenBalance}");

        // Alternatively, you can get precise SOL balance directly from Solana RPC
        // We provide a GetBalanceAsync method for that, but you can make any RPC call you want
        // with AppKit.Solana.RpcRequestAsync<T>
        var balanceFromRpc = await AppKit.Solana.GetBalanceAsync();
        Debug.Log($"RPC SOL balance: {balanceFromRpc}");
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