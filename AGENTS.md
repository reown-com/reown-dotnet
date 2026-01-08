# AI Agent Guidance for Reown .NET

This document provides guidance for AI agents working on the Reown .NET repository.

## Project Overview

Reown .NET is a monorepo containing Unity and NuGet packages for integrating Web3 functionality into applications. The SDK enables developers to connect to blockchain wallets, execute transactions, sign messages, and interact with smart contracts across multiple EVM and Solana blockchain networks.

The repository contains two main product lines:

**AppKit for Unity** - A comprehensive Unity SDK that provides wallet connection UI, blockchain interactions, and cross-platform support (iOS, Android, Windows, macOS, WebGL). It supports 300+ cryptocurrency wallets via the WalletConnect protocol, social logins (Google, X/Twitter, Discord, Apple, GitHub), and both EVM chains (Ethereum, Optimism, Arbitrum, Polygon, Avalanche, Base, Ronin) and Solana.

**WalletKit** - A pure .NET library for building wallet applications that can receive and respond to WalletConnect requests. Published to NuGet for use in non-Unity .NET applications.

The SDK employs a dual-platform strategy: native platforms (iOS, Android, desktop) use Nethereum for full .NET blockchain capabilities, while WebGL builds use a JavaScript bridge to Wagmi/Viem libraries.

### Combined NuGet + UPM Distribution

This repository uses a combined distribution approach:

**NuGet Packages** - Core .NET packages (`Reown.Sign`, `Reown.WalletKit`, `Reown.Core.*`) are published to nuget.org for use in standard .NET applications. These are built from the `Reown.NoUnity.slnf` solution filter.

**Unity Package Manager (UPM)** - Unity packages (`com.reown.appkit.unity`, `com.reown.sign.unity`, etc.) are distributed via OpenUPM and can be installed using the OpenUPM CLI or Unity Package Manager. Git tags in the format `package-name/version` enable direct UPM installation from the repository.

Both distribution channels share the same source code in the `src/` directory. Unity packages include additional Unity-specific code (MonoBehaviours, ScriptableObjects, Editor scripts) alongside the core .NET logic.

## Repository Structure

```
reown-dotnet/
├── src/                              # Source code for all packages
│   ├── Reown.AppKit.Unity/           # Main Unity SDK (highest-level API)
│   ├── Reown.AppKit.Solana.Unity/    # Solana support for Unity
│   ├── Reown.Sign/                   # WalletConnect protocol core
│   ├── Reown.Sign.Unity/             # Unity-specific signing
│   ├── Reown.Sign.Nethereum/         # Ethereum integration for .NET
│   ├── Reown.Sign.Nethereum.Unity/   # Ethereum integration for Unity
│   ├── Reown.Core/                   # Core framework aggregator
│   ├── Reown.Core.Common/            # Shared utilities and logging
│   ├── Reown.Core.Crypto/            # Encryption and key management
│   ├── Reown.Core.Network/           # HTTP client and JSON-RPC
│   ├── Reown.Core.Network.WebSocket/ # WebSocket communication
│   ├── Reown.Core.Storage/           # Data persistence abstractions
│   ├── Reown.WalletKit/              # Wallet SDK for .NET
│   ├── Reown.Unity.Dependencies/     # External dependency aggregator
│   └── Directory.Build.props         # Central version and build config
│
├── sample/                           # Main sample application
│   └── Reown.AppKit.Unity/           # Primary sample app used for testing and demos
│
├── playground/                       # Additional Unity sample projects
│   ├── Reown.SolanaCore.Unity/       # Solana integration example
│   ├── Reown.SolanaSdk.Unity/        # Solana Unity SDK adapter example
│   ├── Reown.AbstractSample.Unity/   # Abstract wallet sample
│   └── ...                           # Other experimental samples
│
├── test/                             # Test projects
│   ├── Reown.Core.Common.Test/
│   ├── Reown.Core.Crypto.Test/
│   ├── Reown.Core.Network.Test/
│   ├── Reown.Core.Storage.Test/
│   ├── Reown.Sign.Test/
│   ├── Reown.WalletKit.Test/
│   └── Rown.TestUtils/               # Shared test utilities (note: typo in actual directory name)
│
├── .github/
│   ├── workflows/                    # CI/CD automation
│   │   ├── unity-build-test.yml      # Unity builds (Windows/Android/WebGL)
│   │   ├── dotnet-build-test.yml     # .NET testing
│   │   ├── release.yml               # NuGet + Git tag releases
│   │   └── sync-unity-package-version.yml
│   ├── actions/                      # Reusable GitHub Actions
│   └── scripts/                      # Build automation scripts
│
├── Reown.sln                         # Full solution (includes Unity projects)
├── Reown.NoUnity.slnf                # Solution filter for .NET-only packages
└── Directory.Packages.props          # Central NuGet package versions
```

## Key Commands

### Building

Build the .NET packages (excludes Unity-specific code):
```bash
dotnet build Reown.NoUnity.slnf --no-restore
```

Build in Release mode:
```bash
dotnet build Reown.NoUnity.slnf -c Release --restore
```

### Testing

Run unit tests:
```bash
dotnet test Reown.NoUnity.slnf --verbosity minimal --filter Category=unit
```

Run integration tests (requires PROJECT_ID environment variable):
```bash
PROJECT_ID=your_project_id dotnet test -m:1 Reown.NoUnity.slnf --verbosity normal --filter Category=integration
```

### Packaging

Create NuGet packages:
```bash
dotnet pack Reown.NoUnity.slnf -c Release --no-build --output .
```

### Unity Development

Unity packages can be built and tested locally if you have Unity installed. To build Unity packages from the command line or IDE, set the `UnityDllPath` MSBuild property to point to your Unity installation's `UnityEngine.dll`:

```bash
dotnet build Reown.sln -p:UnityDllPath="/path/to/Unity/Editor/Data/Managed/UnityEngine.dll"
```

The CI pipeline handles Unity builds for Windows, Android, and WebGL platforms, as well as playmode and editmode tests using Unity 6000.2.6f2.

### Sample Application

The main sample application at `sample/Reown.AppKit.Unity/` is used for testing and demonstrating SDK features. It references all packages via local file paths in its `Packages/manifest.json`, making it ideal for development and testing changes.

The sample demonstrates wallet connection via WalletConnect, social logins (Google, X, Discord, Apple, GitHub), multi-chain support, session management, message signing, transactions, balance queries, contract reading, and network switching.

Key scripts in the sample:
- `AppInit.cs` - App setup, debug console, analytics configuration
- `AppKitInit.cs` - AppKit initialization with project ID, chains, social providers
- `Dapp.cs` - Main UI and interaction logic demonstrating SDK features

To test locally:
1. Open `sample/Reown.AppKit.Unity` in Unity 2022.3+
2. Open the Init scene
3. Press Play to test in the editor

The sample is also deployed to Vercel on each PR for WebGL testing, and test builds are available on Firebase (Android) and TestFlight (iOS).

## Architecture Overview

### Package Hierarchy

The packages follow a layered architecture:

**Core Layer** - Foundation packages shared by all higher-level packages:
- `Reown.Core.Common` - Utilities, logging, error types, hex encoding
- `Reown.Core.Crypto` - X25519 key exchange, ChaCha20Poly1305 encryption, Ed25519 signing
- `Reown.Core.Network` - HTTP client, JSON-RPC handling
- `Reown.Core.Network.WebSocket` - WebSocket client for relay communication
- `Reown.Core.Storage` - `IKeyValueStorage` abstraction with multiple implementations
- `Reown.Core` - Aggregates all core packages

**Sign Layer** - WalletConnect v2 protocol implementation:
- `Reown.Sign` - Core protocol logic, session management, pairing
- `Reown.Sign.Unity` - Unity-specific adaptations (deep linking, coroutines)
- `Reown.Sign.Nethereum` - Nethereum interceptor for signing via WalletConnect
- `Reown.Sign.Nethereum.Unity` - Unity-specific Nethereum integration

**Product Layer** - End-user SDKs:
- `Reown.AppKit.Unity` - Full-featured Unity SDK with UI components
- `Reown.AppKit.Solana.Unity` - Solana blockchain support
- `Reown.WalletKit` - SDK for building wallet applications

### Key Architectural Patterns

**Connector System** - The `Connector` abstract class defines the interface for wallet connection methods. Implementations include `WalletConnectConnector` (native platforms), `WebGlConnector` (browser), and `ProfileConnector` (social login).

**Platform Abstraction** - `EvmService` provides an abstract interface for blockchain operations. `NethereumEvmService` handles native platforms using the Nethereum library, while `WagmiEvmService` delegates to JavaScript via P/Invoke on WebGL.

**Controller Pattern** - Controllers manage specific concerns: `AccountController` (connected accounts), `NetworkController` (active chain), `ModalController` (UI state), `ConnectorController` (wallet connections).

### Version Management

The single source of truth for version is `src/Directory.Build.props`:
```xml
<DefaultVersion>1.5.2</DefaultVersion>
```

The `.github/scripts/sync-unity-version.csx` script propagates this version to all Unity `package.json` files and C# fields marked with `[VersionMarker]`.

### Target Frameworks

.NET packages target: `net7.0`, `net8.0`, `netstandard2.1`

C# language version is set to 9.0 for Unity IL2CPP compatibility.

## Development Notes

### Code Style

- All new classes, methods, and exceptions must have XML documentation comments with clear descriptions
- Follow existing code conventions in each file
- Use existing libraries and utilities rather than adding new dependencies
- Place imports at the top of files
- Avoid using `Any`, `getattr`, `setattr`, or similar dynamic access patterns

### Testing Requirements

- Tests use xUnit framework
- Unit tests are marked with `[Trait("Category", "unit")]`
- Integration tests are marked with `[Trait("Category", "integration")]`
- When modifying code, check if corresponding tests exist and add tests for new functionality

### Unity Considerations

- Unity packages use Unity Package Manager (UPM) format with `package.json` files
- Check Unity `package.json` files to understand the dependency tree before moving code between packages
- Unity CI checks validate that packages build correctly - CI failures indicate real dependency violations, compile errors, or failing tests
- WebGL builds use JavaScript interop via `AppKit.jslib` which bridges to Wagmi/Viem
- Unity 2022.3+ is required; IL2CPP code stripping level should be set to Minimal
- Gamma color space is recommended for best visual results

### CI/CD Pipeline

**Pull Request Validation:**
- .NET unit and integration tests on Windows
- Unity builds for Windows, Android, WebGL
- Unity playmode and editmode tests
- SonarCloud code quality analysis
- Claude AI code review
- WebGL deployment to Vercel with PR comment

**Release Process (on merge to main):**
- Build and publish NuGet packages to nuget.org
- Create Git tags for Unity packages (format: `package-name/version`)

Note: The repository uses a gitflow-style workflow where `develop` is the integration branch and `main` is the release branch. PRs typically target `develop`, and releases are triggered when `develop` is merged into `main`.

### PR Guidelines

- Use conventional commit format for PR titles (e.g., `feat: add feature`, `fix: resolve bug`, `chore: update deps`)
- Never force push or amend commits
- Wait for CI checks to pass before requesting review
- Link the PR and any preview deployments when reporting completion
