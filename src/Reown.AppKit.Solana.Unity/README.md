## Reown.AppKit.Solana.Unity

Adapter that connects Reown AppKit to the Solana Unity SDK. It provides an AppKit-backed `WalletBase` so you can keep using `Web3` from `Solana.Unity.SDK` while all signing and session management go through AppKit.

- **Solana SDK**: [magicblock-labs/Solana.Unity-SDK](https://github.com/magicblock-labs/Solana.Unity-SDK)
- **AppKit docs**: [AppKit for Unity · Installation](https://docs.reown.com/appkit/unity/core/installation)

### What this adapter does

- Installs an AppKit-powered wallet (`AppKitWalletBase`) and wires it into `Web3` via `web3.WalletBase`.
- Hooks AppKit events (`AccountConnected`, `AccountChanged`) to keep `Web3.Account` in sync.
- Proxies signing to AppKit:
  - Transactions: `AppKit.Solana.SignTransactionAsync`, `SignAllTransactionsAsync`
  - Messages: `AppKit.Solana.SignMessageAsync`

### Requirements

- `Solana.Unity-SDK` in your Unity project ([repo](https://github.com/magicblock-labs/Solana.Unity-SDK)).
- Reown AppKit for Unity set up in your scene (follow the [docs](https://docs.reown.com/appkit/unity/core/installation)).

## Quick start

Initialize AppKit once on startup, then use the extension methods on `Web3`.

```csharp
using Reown.AppKit.Unity;
using Reown.Sign.Unity;
using Solana.Unity.SDK;
using UnityEngine;

public class Bootstrap : MonoBehaviour
{
    [SerializeField] private Web3 web3;

    private async void Start()
    {
        var config = new AppKitConfig
        {
            projectId = "YOUR_PROJECT_ID",
            metadata = new Metadata(
                name: "My Solana Game",
                description: "Solana + AppKit",
                url: "https://example.com",
                iconUrl: "https://example.com/icon.png",
                new RedirectData { Native = "mygame://" }
            ),
            supportedChains = new[]
            {
                ChainConstants.Chains.Solana,
                ChainConstants.Chains.SolanaDevNet,
            }
        };

        await AppKit.InitializeAsync(config);
    }
}
```

For full setup (prefab, logging, Android/iOS/WebGL notes), see the [AppKit docs](https://docs.reown.com/appkit/unity/core/installation).

## Using the extension methods

The adapter exposes helpers on `Web3` (see `Runtime/SolanaExtensions.cs`).

### Try to resume an AppKit session

```csharp
var (resumed, account) = await web3.TryResumeAppKitSession();
Debug.Log($"Resumed: {resumed}, Address: {account?.PublicKey}");
```

### Login with AppKit (default UI)

```csharp
var account = await web3.LoginAppKit();
Debug.Log($"Connected: {account.PublicKey}");
```

### Login with a specific wallet

```csharp
// Connect directly to a specific wallet:
// Pass a wallet id from walletguide.walletconnect.network
var account = await web3.LoginAppKit(walletId: "0ef262ca2a56b88d179c93a21383fee4e135bd7bc6680e5c2356ff8e38301037");
```

## After login: keep using Solana.Unity SDK

Once you call `LoginAppKit` (or a resume succeeds), the adapter sets `web3.WalletBase = AppKitWalletBase`. From that point:

- Existing `Web3` calls keep working.
- Any required signatures are requested through AppKit.

Examples:

```csharp
// Sign a message
var bytes = System.Text.Encoding.UTF8.GetBytes("Hello AppKit");
var signature = await Web3.Wallet.SignMessage(bytes);
```

## Notes

- If `web3.customRpc` is not set, the adapter will use the RPC provider provided by Reown.
