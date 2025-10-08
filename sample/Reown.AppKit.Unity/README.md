# AppKit Unity Sample

This repository contains the official Unity sample for [Reown AppKit](https://docs.reown.com/appkit/unity/core/installation). It demonstrates wallet connection, message signing, transactions, balance queries, contract reads, and runtime network switching across multiple EVM chains. Use it as a reference implementation and for local testing.

## What This Sample Covers

This sample demonstrates:

- **Wallet connection**: Connect via WalletConnect
- **Social Logins**: Google, X (Twitter), Discord, Apple, and GitHub authentication
- **Multi-Chain Support**: Ethereum, Optimism, Arbitrum, Ronin, Avalanche, Base, and Polygon
- **Session Management**: Automatic session resumption across app restarts
- **Signing Operations**: Personal sign for messages
- **Transactions**: Send ETH and interact with smart contracts
- **Balance Queries**: Check wallet balances using Reown Blockchain API
- **Contract Reading**: Read data from smart contracts (demonstrated with WCT staking contract)
- **Network Switching**: Change between supported chains at runtime
- **SIWE (optional)**: One-Click Auth, disabled by default (see below)

## Key Scripts

### `AppInit.cs`

Handles initial app setup including debug console initialization and analytics configuration. Also provides useful console commands for debugging:

- `accounts` - Lists all connected accounts
- `sessionProps` - Prints WalletConnect session properties
- `session` - Prints active WalletConnect session
- `webwallet <url>` - Set custom web wallet URL

### `AppKitInit.cs`

The main initialization script for AppKit. This is where we configure:

- **Project ID** and metadata
- **Supported chains** and networks
- **Social login providers**
- **Custom wallets** (platform-specific test wallets from Reown)
- **SIWE configuration** (commented out by default)

This script initializes AppKit and loads the main menu scene.

### `Dapp.cs`

Contains the main UI and interaction logic. Demonstrates various AppKit features through buttons:

- Connect wallet
- Switch networks
- View account details
- Sign messages
- Send transactions
- Get balance
- Read smart contracts
- Disconnect

Each button handler shows best practices for error handling and user feedback.

## Getting Started

### Prerequisites

- Unity 2022.3 or above
- IL2CPP code stripping level: **Minimal** (or lower)
- Gamma color space for the best look

### Quick Start

1. **Open the project** in Unity
2. **Open the Init scene** (usually the first scene in Build Settings)
3. **Press Play** in the Unity Editor
4. **Click "Connect"** to open the AppKit modal and connect a wallet

The sample will automatically try to resume your previous session when you restart.

## Configuration Notes

### Project ID Limitations

This sample comes with a **pre-configured sample Project ID** that is set up for specific bundle IDs and package names.

**For production use or if you change the bundle ID/package name**, you need to:

1. Create your own Project ID at [Reown Dashboard](https://dashboard.reown.com/)
2. Configure your app's bundle ID (iOS) and package name (Android) in the dashboard
3. Replace the `projectId` in `AppKitInit.cs` with your new Project ID

### Platform-Specific Setup

#### Android Build Issues

If your Android build crashes on startup:

1. Go to **Tools → Sentry** in Unity menu
2. **Uncheck "Enable Sentry"**
3. Rebuild the project

This is a known issue with the Sentry integration in the sample that will be fixed in the future.

## Testing SIWE & One-Click Auth

The sample includes **Sign-In with Ethereum (SIWE)** support, but it's commented out by default.

To enable SIWE:

1. Open `AppKitInit.cs`
2. Uncomment the `SiweConfig` section (lines 27-39)
3. Update the `domain` and `uri` to match your application
4. Uncomment the `siweConfig = siweConfig` line in `AppKitConfig` (line 47)

For more details, see the [SIWE documentation](https://docs.reown.com/appkit/unity/core/siwe).

## Learn More

This sample is designed to get you started quickly, but there's much more to explore:

- **[Installation Guide](https://docs.reown.com/appkit/unity/core/installation)** - Complete setup instructions
- **[Usage Documentation](https://docs.reown.com/appkit/unity/core/usage)** - Detailed API reference and examples
- **[Options](https://docs.reown.com/appkit/unity/core/options)** - Configure AppKit behavior
- **[Actions](https://docs.reown.com/appkit/unity/core/actions)** - Available operations and methods
- **[Events](https://docs.reown.com/appkit/unity/core/events)** - Listen to wallet and network changes
- **[Customization](https://docs.reown.com/appkit/unity/core/customization)** - Customize the modal UI
- **[SIWE](https://docs.reown.com/appkit/unity/core/siwe)** - One-Click Auth implementation

## Getting Help

- **[GitHub Issues](https://github.com/reown-com/reown-dotnet/issues)** - Report bugs or request features
- **[Reown Dashboard](https://dashboard.reown.com/)** - Manage your projects
- **[Documentation](https://docs.reown.com/appkit/unity/core/installation)** - Full AppKit Unity documentation

---

Thank you for your interest in the AppKit Unity sample 🙇‍♂️
