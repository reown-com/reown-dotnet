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
│   ├── Reown.Core.Network.WebSocket/ # WebSocket communication (NuGet-only, no UPM package)
│   ├── Reown.Core.Storage/           # Data persistence abstractions
│   ├── Reown.WalletKit/              # Wallet SDK for .NET
│   ├── Reown.Unity.Dependencies/     # External dependency aggregator for Unity
│   └── Directory.Build.props         # Central version and build config
│
├── sample/                           # Main sample application
│   └── Reown.AppKit.Unity/           # Primary sample app used for testing and demos
│
├── playground/                       # Additional Unity sample projects (10 total)
│   ├── Reown.AbstractSample.Unity/   # Standard AppKit demo
│   ├── Reown.Customization.Unity/    # Custom UI/branding demo
│   ├── Reown.Playground.Unity/       # General experimentation sandbox
│   ├── Reown.RoninSample.Unity/      # Ronin blockchain demo
│   ├── Reown.SeiSample.Unity/        # Sei blockchain demo
│   ├── Reown.SmartSession.Unity/     # Smart account/session demo
│   ├── Reown.SolanaCore.Unity/       # Solana core integration demo
│   ├── Reown.SolanaSdk.Unity/        # Solana Unity SDK adapter demo
│   ├── Reown.UniTask.Unity/          # UniTask/async patterns demo
│   └── Reown.ZKCandySample.Unity/    # Zero-knowledge proofs demo
│
├── test/                             # Test projects (target net8.0 only)
│   ├── Reown.Core.Common.Test/       # Note: excluded from Reown.NoUnity.slnf
│   ├── Reown.Core.Crypto.Test/
│   ├── Reown.Core.Network.Test/
│   ├── Reown.Core.Storage.Test/
│   ├── Reown.Sign.Test/
│   ├── Reown.WalletKit.Test/
│   └── Rown.TestUtils/               # Shared test utilities (note: typo in actual directory name)
│
├── .github/
│   ├── workflows/                    # CI/CD automation
│   │   ├── unity-build-test.yml      # Unity builds (Windows/Android/WebGL) + Vercel deploy
│   │   ├── dotnet-build-test.yml     # .NET unit and integration tests
│   │   ├── release.yml               # NuGet + Git tag releases (on merge to main)
│   │   ├── sync-unity-package-version.yml  # Version propagation
│   │   ├── sonarcloud.yml            # Code quality analysis
│   │   ├── claude-review.yml         # AI code review on PRs
│   │   └── cta.yml                   # CTA assistant automation
│   ├── actions/                      # Reusable GitHub Actions
│   │   └── test-dotnet/              # Composite action for running .NET tests
│   └── scripts/                      # Build automation scripts (.csx)
│       ├── get-version.csx           # Reads version from Directory.Build.props
│       ├── get-unity-package-names.csx  # Lists all UPM package names
│       ├── sync-unity-version.csx    # Propagates version to package.json and [VersionMarker] fields
│       └── logging.csx              # Shared logging utilities
│
├── Reown.slnx                        # Full solution in .slnx format (includes Unity projects)
├── Reown.NoUnity.slnf                # Solution filter for .NET-only packages (references Reown.slnx)
└── Directory.Packages.props          # Central NuGet package versions (ManagePackageVersionsCentrally)
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
dotnet build Reown.slnx -p:UnityDllPath="/path/to/Unity/Editor/Data/Managed/UnityEngine.dll"
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
- `Reown.Core.Common` - Utilities, logging (`ILogger`), error types, hex encoding, `VersionMarkerAttribute`
- `Reown.Core.Crypto` - X25519 key exchange, ChaCha20Poly1305 encryption, Ed25519 signing
- `Reown.Core.Network` - HTTP client, JSON-RPC handling (`IJsonRpcProvider`)
- `Reown.Core.Network.WebSocket` - WebSocket client for relay communication (NuGet-only, not distributed via UPM)
- `Reown.Core.Storage` - `IKeyValueStorage` abstraction with multiple implementations
- `Reown.Core` - Aggregates all core packages
- `Reown.Unity.Dependencies` - Aggregates external NuGet dependencies for Unity (BouncyCastle, Newtonsoft.Json)

**Sign Layer** - WalletConnect v2 protocol implementation:
- `Reown.Sign` - Core protocol logic, session management, pairing, `AddressProvider`
- `Reown.Sign.Unity` - Unity-specific adaptations (deep linking, coroutines, `SignClientUnity`)
- `Reown.Sign.Nethereum` - Nethereum interceptor for signing via WalletConnect
- `Reown.Sign.Nethereum.Unity` - Unity-specific Nethereum integration (`ReownSignUnityInterceptor`)

**Product Layer** - End-user SDKs:
- `Reown.AppKit.Unity` - Full-featured Unity SDK with UI components, all controllers, connectors, and services
- `Reown.AppKit.Solana.Unity` - Solana blockchain support (depends on `com.solana.unity_sdk`)
- `Reown.WalletKit` - SDK for building wallet applications

### Key Architectural Patterns

**Template Method** - The primary pattern across the codebase. Abstract classes expose public methods that validate inputs and delegate to protected abstract `*Core` methods. Used in `Connector`, `EvmService`, `SolanaService`, `ModalController`, and `NetworkController`.

**Singleton (AppKit)** - `AppKit` is an abstract `MonoBehaviour` with a static `Instance` property. `AppKitCore` is the concrete implementation that initializes as a singleton via `DontDestroyOnLoad`. It provides static access to all controllers and services.

**Connector System** - The `Connector` abstract class (`src/Reown.AppKit.Unity/Runtime/Connectors/Connector.cs`) defines the interface for wallet connection methods. Key implementations:
- `WalletConnectConnector` - Native platforms (iOS, Android, desktop)
- `WebGlConnector` - Browser/WebGL (conditional compilation: `#if UNITY_WEBGL`)
- `ProfileConnector` - Social login; inherits from `WalletConnectConnector` (not directly from `Connector`), adds email/username/provider and smart account support
- `ConnectorController` - Itself inherits from `Connector` and acts as a facade/aggregator over all registered connectors, routing calls to the active one

**Platform Abstraction** - Abstract service classes with platform-specific implementations:
- `EvmService` (`Runtime/Evm/EvmService.cs`) → `NethereumEvmService` (native, uses Nethereum library) or `WagmiEvmService` (WebGL, delegates to JavaScript via P/Invoke)
- `SolanaService` (`Runtime/Solana/SolanaService.cs`) → Native-only, uses `ValueTask` for async operations

**Controller Pattern** - Controllers live in `src/Reown.AppKit.Unity/Runtime/Controllers/` and manage specific concerns:
- `AccountController` - Connected accounts, balance, profile data; implements `INotifyPropertyChanged` for UI binding
- `NetworkController` (abstract) → `NetworkControllerCore` - Active chain management, chain switching
- `ModalController` (abstract) → `ModalControllerUtk` (UIToolkit) or `ModalControllerWebGl` - UI modal state
- `ConnectorController` - Wallet connection facade (see Connector System above)
- `ApiController` - API client for Reown cloud services
- `BlockchainApiController` - Blockchain-specific API calls (balance, token data)
- `EventsController` - Analytics event tracking (Pulse integration)
- `SiweController` - Sign In With Ethereum protocol
- `RouterController` - View routing logic
- `NotificationController` - Toast/notification management

**WebGL JavaScript Bridge** - Two `.jslib` files provide JavaScript interop via P/Invoke:
- `src/Reown.AppKit.Unity/Plugins/AppKit.jslib` - Bridges C# to Wagmi/Viem for blockchain operations
- `src/Reown.Sign.Unity/Plugins/ReownWebSocket.jslib` - Custom WebSocket implementation for browser
- `WagmiInterop` and `ModalInterop` static classes manage the C#→JS communication using `InteropService`

**Initialization Flow** - `AppKitCore.InitializeAsyncCore()` creates all controllers and services sequentially, then initializes them in parallel via `Task.WhenAll`. Platform-specific services are selected at runtime using `#if UNITY_WEBGL` conditional compilation.

### Version Management

The single source of truth for version is `src/Directory.Build.props`:
```xml
<DefaultVersion>1.5.2</DefaultVersion>
```

The `.github/scripts/sync-unity-version.csx` script propagates this version to:
- All Unity `package.json` files (version field and `com.reown.*` dependency versions)
- C# fields marked with `[VersionMarker]` attribute (defined in `Reown.Core.Common/Runtime/Utils/VersionMarkerAttribute.cs`)
- `packages-lock.json` files
- `ProjectSettings.asset` (bundleVersion)

Current `[VersionMarker]` usage:
- `AppKit.Version` in `src/Reown.AppKit.Unity/Runtime/AppKit.cs` (format: `"unity-appkit-v{version}"`)
- `SignMetadata.Version` in `src/Reown.Sign.Unity/Runtime/SignMetadata.cs` (format: `"v{version}"`)

### Target Frameworks

.NET packages target: `net7.0`, `net8.0`, `netstandard2.1`

Test projects target: `net8.0` only (with `ImplicitUsings` and `Nullable` enabled)

C# language version is set to 9.0 for Unity IL2CPP compatibility.

### Key Dependencies

Managed centrally in `Directory.Packages.props`. Key libraries:
- **Nethereum** (Web3, HdWallet, Signer) - Ethereum blockchain interactions
- **BouncyCastle.Cryptography** - Cryptographic operations
- **Newtonsoft.Json** - JSON serialization
- **Websocket.Client** - WebSocket relay communication
- **ZXing.Net** - QR code generation
- **xunit** + **Microsoft.NET.Test.Sdk** - Test framework

### UPM Dependency Tree

```
com.reown.appkit.unity
├── com.reown.sign.nethereum.unity
│   ├── com.reown.sign.nethereum
│   │   ├── com.reown.sign
│   │   │   └── com.reown.core
│   │   │       ├── com.reown.core.common
│   │   │       ├── com.reown.core.network
│   │   │       ├── com.reown.core.storage
│   │   │       ├── com.reown.core.crypto
│   │   │       └── com.reown.unity.dependencies
│   │   └── com.nethereum.unity [external]
│   └── com.reown.sign.unity
│       └── com.reown.sign
├── com.reown.sign.unity
├── com.reown.core
├── com.reown.unity.dependencies
└── com.unity.vectorgraphics [external]
```

## Development Notes

### Code Style

- All new classes, methods, and exceptions must have XML documentation comments with clear descriptions
- Follow existing code conventions in each file
- Use existing libraries and utilities rather than adding new dependencies
- Place `using` directives at the top of files (ImplicitUsings is disabled in src projects)
- C# language version is 9.0 — do not use C# 10+ features (file-scoped namespaces, global usings, etc.) as they are incompatible with Unity IL2CPP/AOT compilation
- Use centralized package versioning: add new NuGet dependencies to `Directory.Packages.props`, not individual `.csproj` files

### Testing Requirements

- Tests use xUnit framework with Microsoft.NET.Test.Sdk, targeting `net8.0` only
- Unit tests are marked with `[Trait("Category", "unit")]`
- Integration tests are marked with `[Trait("Category", "integration")]` and require `PROJECT_ID` environment variable
- Integration tests run single-threaded (`-m:1`) to coordinate relay communication
- `Reown.Sign.Test` has custom `xunit.runner.json`: parallelization disabled, `stopOnFail: true`, `longRunningTestSeconds: 250`
- `Rown.TestUtils` provides shared fixtures: `TwoClientsFixture<T>` (two-client dapp↔wallet scenarios), `CryptoWalletFixture` (Nethereum HD wallet), `TestOutputHelperLogger` (xUnit→Reown logger bridge), `TempFolder` (auto-cleanup temp dirs)
- `Reown.Core.Common.Test` exists in `Reown.slnx` but is excluded from `Reown.NoUnity.slnf` — it won't run with the standard `dotnet test Reown.NoUnity.slnf` command
- When modifying code, check if corresponding tests exist and add tests for new functionality

### Unity Considerations

- Unity packages use Unity Package Manager (UPM) format with `package.json` files — 13 UPM packages total in `src/`
- Most packages have an `.asmdef` (assembly definition) file in their `Runtime/` directory that defines the compilation unit and inter-package dependencies via GUIDs
- Check Unity `package.json` files to understand the dependency tree before moving code between packages
- `Reown.Core.Network.WebSocket` has no `package.json` — it is NuGet-only and not distributed as a UPM package
- Unity CI checks validate that packages build correctly - CI failures indicate real dependency violations, compile errors, or failing tests
- WebGL builds use two JavaScript interop files: `AppKit.jslib` (Wagmi/Viem bridge) and `ReownWebSocket.jslib` (browser WebSocket)
- Platform-specific code uses `#if UNITY_WEBGL` conditional compilation throughout connectors and services
- Unity 2022.3+ is required; CI uses Unity 6000.2.6f2; IL2CPP code stripping level should be set to Minimal
- Gamma color space is recommended for best visual results
- The sample app at `sample/Reown.AppKit.Unity/` references all packages via local `file:` paths in `Packages/manifest.json`

### CI/CD Pipeline

**Pull Request Validation:**
- .NET unit and integration tests on Windows (`dotnet-build-test.yml`) — requires .NET 9.0.x and 8.0.x SDKs
- Unity builds for Windows, Android, WebGL using `game-ci/unity-builder` v4.5 (`unity-build-test.yml`)
- Unity playmode and editmode tests using `game-ci/unity-test-runner` v4.3.1
- WebGL deployment to Vercel with PR comment (runs after Unity build job)
- SonarCloud code quality analysis with coverage collection (`sonarcloud.yml`)
- Claude AI code review on PRs targeting `develop`, or triggered by `@claude review` comment (`claude-review.yml`)
- Version sync: when `src/Directory.Build.props` changes, `sync-unity-package-version.yml` auto-commits version updates

**Release Process (on merge to main via `release.yml`):**
- **nuget job**: Build Release → Pack → Push `*.nupkg` to nuget.org
- **unity job** (parallel): Create Git tags for each UPM package (format: `package-name/version`, e.g., `com.reown.appkit.unity/1.5.2`) and push to origin
- Both jobs run concurrently with `cancel-in-progress: true`

**Dependency Automation (`dependabot.yml`):**
- NuGet: Weekly on Mondays at 04:00 UTC, targeting `develop`, max 10 PRs, prefix `deps`
- GitHub Actions: Same schedule, max 5 PRs, prefix `ci`

**Code Ownership:** Single owner `@skibitsky` (`.github/CODEOWNERS`)

Note: The repository uses a gitflow-style workflow where `develop` is the integration branch and `main` is the release branch. PRs typically target `develop`, and releases are triggered when `develop` is merged into `main`.

### PR Guidelines

- Use conventional commit format for PR titles (e.g., `feat: add feature`, `fix: resolve bug`, `chore: update deps`)
- Never force push or amend commits
- Wait for CI checks to pass before requesting review
- Link the PR and any preview deployments when reporting completion
