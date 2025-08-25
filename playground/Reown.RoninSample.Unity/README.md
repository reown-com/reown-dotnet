# Reown.RoninSample.Unity

This sample demonstrates integrating Reown AppKit with the Ronin chain and Ronin Wallet across mobile and web.

## Ronin Wallet Connectivity

Reown AppKit supports all current Ronin Wallet platforms and provides multiple connection options that users can choose from. Game integrates once by initializing AppKit, no additional code is required per connection method.

### Mobile Ronin Wallet

- WalletConnect QR code: display a QR code in the game; the Ronin mobile app scans it to establish the session.
- Same-device deep link: when the game runs on the same mobiledevice as Ronin Wallet, AppKit deep links to complete the connection.
- In-app browser: when opened in the built-in browser inside mobile Ronin Wallet, AppKit connects within that environment.

### Ronin Wallet Browser Extension

- When the Ronin browser extension is installed, AppKit detects it and enables a direct connection.

See `Assets/Scripts/Main.cs` for a minimal initialization example.

## One-Click Auth

Mobile Ronin Wallet supports one-click auth, which allows users to connect their wallet and sign a message in one step.

For more information, see [One-Click Auth](https://docs.reown.com/appkit/unity/core/siwe#one-click-auth).
