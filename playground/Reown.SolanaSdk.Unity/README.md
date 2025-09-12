# Reown.SolanaSdk.Unity

A test project demonstrating the integration between [Solana.Unity-SDK](https://github.com/magicblock-labs/Solana.Unity-SDK) and Reown AppKit.

This project is based on the official [Solana.Unity-SDK](https://github.com/magicblock-labs/Solana.Unity-SDK) sample.

Reown AppKit has been integrated into the `wallet_scene`, with [updated external wallet connection logic](https://github.com/reown-com/reown-dotnet/blob/feat/solana-network/playground/Reown.SolanaSdk.Unity/Assets/Solana%20Wallet/Scripts/example/screens/LoginScreen.cs#L90-L106) that:

- Connects to Jupiter mobile wallet on mobile platforms
- Shows the AppKit wallet selection modal on desktop platforms
