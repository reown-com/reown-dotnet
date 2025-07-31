using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
                name: "My Game",
                description: "Short description",
                url: "https://example.com",
                iconUrl: "https://example.com/logo.png"
            ),
            supportedChains = new[]
            {
                ChainConstants.Chains.Sei,
                ChainConstants.Chains.SeiTestnet,
            }
        };
        // Initialize AppKit with config
        await AppKit.InitializeAsync(config);
        
        await ResumeSessionOrOpenAppKitModal();
    }

    public async Task ResumeSessionOrOpenAppKitModal()
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
            // Wait for the user to connect their account
            AppKit.AccountConnected += (_, e) => MyAccountConnectedHandler();
        }
    }
    
    // Called when previous session resumed or when user connected account from AppKit modal UI
    private void MyAccountConnectedHandler()
    {
        Debug.Log("Account connected");

#if UNITY_EDITOR
        // Log WalletConnect session for debugging
        var session = (AppKit.ConnectorController.ActiveConnector as WalletConnectConnector).SignClient.AddressProvider.DefaultSession;
        Debug.Log(JsonConvert.SerializeObject(session, Formatting.Indented));
#endif
        
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

        const string message = "Hello, Sei!";
        
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
