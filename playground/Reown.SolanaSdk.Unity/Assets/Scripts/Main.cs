using System.Threading.Tasks;
using Reown.AppKit.Unity;
using Reown.Core.Common.Logging;
using Reown.Sign.Unity;
using Solana.Unity.SDK;
using UnityEngine;
using UnityEngine.UI;

public class Main : MonoBehaviour
{
    [SerializeField] private Web3 _web3;

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
                name: "Solana Sdk",
                description: "Testing Solana.Unity-Sdk + Reown AppKit",
                url: "https://reown.com",
                iconUrl: "https://raw.githubusercontent.com/reown-com/reown-dotnet/refs/heads/develop/sample/Reown.AppKit.Unity/Assets/Textures/appkit-icon-unity.png",
                new RedirectData
                {
                    Native = "solanasdktest://"
                }
            ),
            supportedChains = new[]
            {
                ChainConstants.Chains.Solana,
                ChainConstants.Chains.SolanaDevNet, // note, that few mobile wallets support devnet
            }
        };
        
        // Initialize AppKit with config
        await AppKit.InitializeAsync(config);
        
        // Try to resume AppKit session
        var (resumed, _) = await _web3.TryResumeAppKitSession();
        Debug.Log($"AppKit session resumed: {resumed}");
    }
}
