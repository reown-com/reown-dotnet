# Tomicz Simple Example Unity

A minimal, learning-focused Unity sample project for the Reown AppKit SDK. This sample provides essential wallet integration functionality in a single, easy-to-understand script.

## 🎯 Purpose

This sample is designed for developers who want to:

- **Quickly learn** core AppKit concepts
- **Get started** with basic wallet integration
- **Understand** essential API patterns
- **Copy-paste** code for their own projects

## 🚀 Quick Start

1. **Open** the project in Unity 2022.3+
2. **Configure** your Project ID in the WalletController inspector
3. **Press Play** and click "Init" to initialize AppKit
4. **Click "Connect"** to open the wallet connection modal
5. **Connect** your wallet and see the address/balance displayed

## 📱 Features

- ✅ **Wallet Connection** - Connect/disconnect wallets
- ✅ **Account Display** - Show connected wallet address
- ✅ **Balance Display** - Display native token balance
- ✅ **Network Switching** - Change blockchain networks
- ✅ **Simple UI** - Clean, minimal interface

## 🔧 WalletController.cs

The main script (`Assets/Scripts/WalletController.cs`) contains all the essential AppKit functionality:

### **Core Methods:**

```csharp
// Initialize AppKit with your project configuration
public void Init()

// Open wallet connection modal
public void Connect()

// Disconnect current wallet
public void Disconnect()

// Get and display account information
public void GetAccount()

// Switch blockchain networks
public void SelectNetwork()
```

### **Key Features:**

- **Single Script** - Everything in one file for easy understanding
- **Event Handling** - Automatic UI updates when wallet connects
- **Error Handling** - Connection status checks and user feedback
- **Balance Updates** - Real-time balance fetching and display
- **State Management** - Button states based on connection status

## 🐛 Debugging & Logs

### **Xcode Console (iOS)**

1. Open Xcode
2. Go to **Window → Devices and Simulators**
3. Select your device
4. Click **Open Console**
5. **Search for "tomicz"** to filter AppKit logs

### **Unity Console**

- All logs are prefixed with `"tomicz:"` for easy filtering
- Look for connection status, errors, and balance updates

### **Common Log Messages:**

```
tomicz: Started initializing AppKit
tomicz: AppKit initialized
tomicz: Account connected successfully
tomicz: Set wallet address: 0x...
tomicz: Set wallet balance: 0.5 ETH
```

## ⚙️ Configuration

### **Project Settings:**

- **Project ID** - Get from [Reown Cloud](https://cloud.reown.com/)
- **Project Name** - Your app name
- **Project Description** - Brief app description
- **Project URL** - Your app website
- **Project Icon URL** - App icon URL

### **Supported Chains:**

- Ethereum (Mainnet & Testnets)
- Polygon, Arbitrum, Optimism, Base
- And more (configurable in AppKitConfig)

## 📦 Dependencies

- **Reown AppKit Unity** (v1.5.0)
- **TextMesh Pro** (v3.0.7)
- **Unity 2022.3+**

## 🎮 UI Components

| Component             | Purpose                          |
| --------------------- | -------------------------------- |
| **Init Button**       | Initialize AppKit                |
| **Connect Button**    | Open wallet connection modal     |
| **Disconnect Button** | Disconnect current wallet        |
| **Network Button**    | Switch blockchain networks       |
| **Account Button**    | Get account details              |
| **Address Text**      | Display connected wallet address |
| **Balance Text**      | Show native token balance        |

## 🔄 Workflow

1. **Initialize** → Click "Init" to set up AppKit
2. **Connect** → Click "Connect" to open wallet modal
3. **Select Wallet** → Choose your preferred wallet
4. **Approve Connection** → Confirm in your wallet
5. **View Details** → See address and balance displayed
6. **Switch Networks** → Use "Network" button to change chains
7. **Disconnect** → Click "Disconnect" when done

## 🐛 Troubleshooting

### **Common Issues:**

**"AppKit not initialized"**

- Make sure you clicked "Init" first
- Check your Project ID is correct

**"Account not connected"**

- Ensure you've connected a wallet
- Check wallet connection status

**"Balance not updating"**

- Verify network connection
- Check if wallet has native tokens

### **Getting Help:**

- Check Unity Console for error messages
- Search logs for "tomicz:" prefix
- Visit [Reown Documentation](https://docs.reown.com/appkit/unity)

---

**Happy Web3 Development! 🚀**
