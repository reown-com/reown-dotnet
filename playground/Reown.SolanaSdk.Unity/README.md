# Reown.SolanaSdk.Unity

A test project demonstrating the integration between [Solana.Unity-SDK](https://github.com/magicblock-labs/Solana.Unity-SDK) and Reown AppKit.

This project is based on the official [Solana.Unity-SDK](https://github.com/magicblock-labs/Solana.Unity-SDK) sample.

Reown AppKit has been integrated into the `wallet_scene`, with [updated external wallet connection logic](https://github.com/reown-com/reown-dotnet/blob/feat/solana-network/playground/Reown.SolanaSdk.Unity/Assets/Solana%20Wallet/Scripts/example/screens/LoginScreen.cs#L90-L106) that:

- Connects to Jupiter mobile wallet on mobile platforms
- Shows the AppKit wallet selection modal on desktop platforms

## Direct Jupiter Wallet Connection

For direct Jupiter wallet connection to work, you'll need to enable the [detection of installaed wallets](https://docs.reown.com/appkit/unity/core/options#enable-installed-wallet-detection) for Jupiter:

### Android

AndroidManifest.xml should include the following:

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">

    <queries>
        <package android:name="ag.jup.jupiter.android"/>
        <!-- Add other wallet package names here -->
    </queries>

    <application>
        ...
    </application>
</manifest>
```

### iOS

iOS Info.plist should include the following:

```xml
<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
    <dict>
        ...

        <key>LSApplicationQueriesSchemes</key>
        <array>
            <string>jupiter</string>
            <!-- Add other wallet schemes here -->
        </array>

        ...
    </dict>
</plist>

```
