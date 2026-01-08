# AI Agent Guidance for Reown .NET

This document provides guidance for AI agents working on the Reown .NET repository.

## Project Overview

Reown .NET is a monorepo containing Unity and NuGet packages for integrating Web3 functionality into applications. The SDK enables developers to connect to blockchain wallets, execute transactions, sign messages, and interact with smart contracts across multiple blockchain networks.

The repository contains two main product lines:

**AppKit for Unity** - A comprehensive Unity SDK that provides wallet connection UI, blockchain interactions, and cross-platform support (iOS, Android, Windows, macOS, WebGL). It supports 300+ cryptocurrency wallets via the WalletConnect protocol.

**WalletKit** - A pure .NET library for building wallet applications that can receive and respond to WalletConnect requests. Published to NuGet for use in non-Unity .NET applications.

The SDK employs a dual-platform strategy: native platforms (iOS, Android, desktop) use Nethereum for full .NET blockchain capabilities, while WebGL builds use a JavaScript bridge to Wagmi/Viem libraries.

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
├── sample/                           # Example Unity application
│   └── Reown.AppKit.Unity/           # Demo project with integration examples
│
├── test/                             # Test projects
│   ├── Reown.Core.Common.Test/
│   ├── Reown.Core.Crypto.Test/
│   ├── Reown.Core.Network.Test/
│   ├── Reown.Core.Storage.Test/
│   ├── Reown.Sign.Test/
│   ├── Reown.WalletKit.Test/
│   └── Rown.TestUtils/               # Shared test utilities
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

Unity packages cannot be built or tested locally without a Unity installation. The CI pipeline handles Unity builds for Windows, Android, and WebGL platforms, as well as playmode and editmode tests.

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

- Unity packages use Unity Package Manager (UPM) format
- Check Unity `package.json` files to understand the dependency tree before moving code between packages
- Unity CI checks validate that packages build correctly - CI failures indicate real issues
- WebGL builds use JavaScript interop via `AppKit.jslib`

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

### PR Guidelines

- Use conventional commit format for PR titles (e.g., `feat: add feature`, `fix: resolve bug`, `chore: update deps`)
- Never force push or amend commits
- Wait for CI checks to pass before requesting review
- Link the PR and any preview deployments when reporting completion
