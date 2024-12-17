using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Util;
using Newtonsoft.Json;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Cacao;
using Reown.Sign.Models.Engine;
using Reown.TestUtils;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

[RpcMethod("personal_sign")]
[RpcRequestOptions(Clock.ONE_MINUTE, 99998)]
[RpcResponseOptionsAttribute(Clock.ONE_MINUTE, 99998)]
public class PersonalSign : List<string>
{
    public PersonalSign(string hexUtf8, string account) : base(new[] { hexUtf8, account })
    {
    }

    [Preserve]
    public PersonalSign()
    {
    }
}

public class AuthenticateTests : IClassFixture<SignClientFixture>, IClassFixture<CryptoWalletFixture>
{
    private readonly SignClientFixture _signClientFixture;
    private readonly CryptoWalletFixture _cryptoWalletFixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public AuthenticateTests(SignClientFixture signClientFixture, CryptoWalletFixture cryptoWalletFixture, ITestOutputHelper testOutputHelper)
    {
        _signClientFixture = signClientFixture;
        _cryptoWalletFixture = cryptoWalletFixture;
        _testOutputHelper = testOutputHelper;
        
        ReownLogger.Instance = new TestOutputHelperLogger(testOutputHelper);
    }

    private static AuthParams GetTestAuthParams(string[]? chains = null, string[]? methods = null)
    {
        return new AuthParams(
            chains ?? ["eip155:1", "eip155:2"],
            "https://reown.com",
            "1",
            "https://reown.com",
            null,
            null,
            null,
            null,
            [
                "https://example.com/my-web2-claim.json",
                "urn:recap:eyJhdHQiOnsiaHR0cHM6Ly9ub3RpZnkud2FsbGV0Y29ubmVjdC5jb20iOnsibWFuYWdlL2FsbC1hcHBzLW5vdGlmaWNhdGlvbnMiOlt7fV19fX0"
            ],
            methods ??
            [
                "personal_sign",
                "eth_chainId",
                "eth_signTypedData_v4"
            ]
        );
    }


    // This test simulates the scenario where the wallet supports all the requested chains and methods
    // and replies with a single signature
    [Fact]
    public async Task TestAuthenticate_SingleSignature_AllChainsAndMethods()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var authParams = GetTestAuthParams();
        
        var tcs = new TaskCompletionSource();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
            Assert.NotNull(args.Payload.RequestId);
            
            // Validate that the dapp has both `session_authenticate` & `session_proposal` stored
            // And expirer configured
            var pendingProposals = dapp.Proposal.Values;
            Assert.Single(pendingProposals);
            Assert.Contains($"id:{pendingProposals[0].Id}", dapp.CoreClient.Expirer.Keys);
            Assert.NotNull(dapp.CoreClient.Expirer.Get(pendingProposals[0].Id));
            Assert.True(dapp.CoreClient.Expirer.Get(pendingProposals[0].Id).Expiry > 0);

            var pendingAuthRequests = dapp.Auth.PendingRequests.Values;
            Assert.Single(pendingAuthRequests);
            Assert.Contains($"id:{pendingAuthRequests[0].Id}", dapp.CoreClient.Expirer.Keys);
            Assert.NotNull(dapp.CoreClient.Expirer.Get(pendingAuthRequests[0].Id));
            Assert.True(dapp.CoreClient.Expirer.Get(pendingAuthRequests[0].Id).Expiry > 0);
            Assert.Equal(args.Payload.RequestId, pendingAuthRequests[0].Id);

            var pendingProposalsWallet = wallet.Proposal.Values;
            Assert.Empty(pendingProposalsWallet);

            args.Payload.Populate(authParams.Chains, authParams.Methods!);
            var iss = $"did:pkh:eip155:1:{_cryptoWalletFixture.WalletAddress}";

            var cacaoPayload = CacaoPayload.FromAuthPayloadParams(args.Payload, iss);
            var message = cacaoPayload.FormatMessage();
            var signature = await _cryptoWalletFixture.SignMessage(message);
            var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
            var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

            var isSignatureValid = await cacao.VerifySignature(wallet.CoreClient.ProjectId);
            Assert.True(isSignatureValid);

            await wallet.ApproveSessionAuthenticate(args.Payload.RequestId.Value, [cacao]);

            tcs.SetResult();
        };

        wallet.SessionProposed += (_, _) => throw new InvalidOperationException("Wallet should not emit session_proposal");

        var authData = await dapp.Authenticate(authParams);
        _ = await wallet.Pair(authData.Uri);

        await tcs.Task;
        
        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();
        
        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);
        
        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = @args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };
        
        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], "eip155:1");
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));
        
        // Confirm that all pending proposals and auth requests have been cleared
        Assert.Empty(wallet.Proposal.Values);
        Assert.Empty(wallet.Auth.PendingRequests.Values);
        Assert.Empty(dapp.Proposal.Values);
        Assert.Empty(dapp.Auth.PendingRequests.Values);

        await _signClientFixture.DisposeAndReset();
    }

    // This test simulates the scenario where the wallet supports subset of the requested chains and all methods
    // and replies with a single signature
    [Fact]
    public async Task TestAuthenticate_SingleSignature_SomeChainsAllMethods()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var requestedChains = new[]
        {
            "eip155:1",
            "eip155:2"
        };
        var supportedChains = requestedChains.Take(1).ToArray();
        var authParams = GetTestAuthParams(requestedChains);

        var tcs = new TaskCompletionSource();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
            Assert.NotNull(args.Payload.RequestId);

            // Validate that the dapp has both `session_authenticate` & `session_proposal` stored
            // And expirer configured
            var pendingProposals = dapp.Proposal.Values;
            Assert.Single(pendingProposals);
            Assert.Contains($"id:{pendingProposals[0].Id}", dapp.CoreClient.Expirer.Keys);
            Assert.NotNull(dapp.CoreClient.Expirer.Get(pendingProposals[0].Id));
            Assert.True(dapp.CoreClient.Expirer.Get(pendingProposals[0].Id).Expiry > 0);

            var pendingAuthRequests = dapp.Auth.PendingRequests.Values;
            Assert.Single(pendingAuthRequests);
            Assert.Contains($"id:{pendingAuthRequests[0].Id}", dapp.CoreClient.Expirer.Keys);
            Assert.NotNull(dapp.CoreClient.Expirer.Get(pendingAuthRequests[0].Id));
            Assert.True(dapp.CoreClient.Expirer.Get(pendingAuthRequests[0].Id).Expiry > 0);
            Assert.Equal(args.Payload.RequestId, pendingAuthRequests[0].Id);

            var pendingProposalsWallet = wallet.Proposal.Values;
            Assert.Empty(pendingProposalsWallet);

            args.Payload.Populate(supportedChains, authParams.Methods!);
            var iss = $"did:pkh:{supportedChains.First()}:{_cryptoWalletFixture.WalletAddress}";

            var cacaoPayload = CacaoPayload.FromAuthPayloadParams(args.Payload, iss);
            var message = cacaoPayload.FormatMessage();
            var signature = await _cryptoWalletFixture.SignMessage(message);
            var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
            var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

            var isSignatureValid = await cacao.VerifySignature(wallet.CoreClient.ProjectId);
            Assert.True(isSignatureValid);

            await wallet.ApproveSessionAuthenticate(args.Payload.RequestId.Value, [cacao]);

            tcs.SetResult();
        };

        wallet.SessionProposed += (_, _) => throw new InvalidOperationException("Wallet should not emit session_proposal");

        var authData = await dapp.Authenticate(authParams);
        _ = await wallet.Pair(authData.Uri);

        await tcs.Task;

        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();

        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);

        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = @args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };

        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], supportedChains.First());
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));

        // Confirm that all pending proposals and auth requests have been cleared
        Assert.Empty(wallet.Proposal.Values);
        Assert.Empty(wallet.Auth.PendingRequests.Values);
        Assert.Empty(dapp.Proposal.Values);
        Assert.Empty(dapp.Auth.PendingRequests.Values);

        await _signClientFixture.DisposeAndReset();
    }

    // This test simulates the scenario where the wallet supports subset of the requested chains and methods
    // and replies with a single signature
    [Fact]
    public async Task TestAuthenticate_SingleSignature_SomeChainsAndMethods()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var requestedChains = new[]
        {
            "eip155:1",
            "eip155:2"
        };
        var supportedChains = requestedChains.Take(1).ToArray();
        var requestedMethods = new[]
        {
            "personal_sign",
            "eth_chainId",
            "eth_signTypedData_v4"
        };
        var supportedMethods = requestedMethods.Take(1).ToArray();
        var authParams = GetTestAuthParams(requestedChains, requestedMethods);

        var tcs = new TaskCompletionSource();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
            Assert.NotNull(args.Payload.RequestId);

            // Validate that the dapp has both `session_authenticate` & `session_proposal` stored
            // And expirer configured
            var pendingProposals = dapp.Proposal.Values;
            Assert.Single(pendingProposals);
            Assert.Contains($"id:{pendingProposals[0].Id}", dapp.CoreClient.Expirer.Keys);
            Assert.NotNull(dapp.CoreClient.Expirer.Get(pendingProposals[0].Id));
            Assert.True(dapp.CoreClient.Expirer.Get(pendingProposals[0].Id).Expiry > 0);

            var pendingAuthRequests = dapp.Auth.PendingRequests.Values;
            Assert.Single(pendingAuthRequests);
            Assert.Contains($"id:{pendingAuthRequests[0].Id}", dapp.CoreClient.Expirer.Keys);
            Assert.NotNull(dapp.CoreClient.Expirer.Get(pendingAuthRequests[0].Id));
            Assert.True(dapp.CoreClient.Expirer.Get(pendingAuthRequests[0].Id).Expiry > 0);
            Assert.Equal(args.Payload.RequestId, pendingAuthRequests[0].Id);

            var pendingProposalsWallet = wallet.Proposal.Values;
            Assert.Empty(pendingProposalsWallet);

            args.Payload.Populate(supportedChains, supportedMethods);
            var iss = $"did:pkh:{supportedChains.First()}:{_cryptoWalletFixture.WalletAddress}";

            var cacaoPayload = CacaoPayload.FromAuthPayloadParams(args.Payload, iss);
            var message = cacaoPayload.FormatMessage();
            var signature = await _cryptoWalletFixture.SignMessage(message);
            var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
            var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

            var isSignatureValid = await cacao.VerifySignature(wallet.CoreClient.ProjectId);
            Assert.True(isSignatureValid);

            await wallet.ApproveSessionAuthenticate(args.Payload.RequestId.Value, [cacao]);

            tcs.SetResult();
        };

        wallet.SessionProposed += (_, _) => throw new InvalidOperationException("Wallet should not emit session_proposal");

        var authData = await dapp.Authenticate(authParams);
        _ = await wallet.Pair(authData.Uri);

        await tcs.Task;

        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();

        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);

        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = @args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };

        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], supportedChains.First());
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));

        // Confirm that all pending proposals and auth requests have been cleared
        Assert.Empty(wallet.Proposal.Values);
        Assert.Empty(wallet.Auth.PendingRequests.Values);
        Assert.Empty(dapp.Proposal.Values);
        Assert.Empty(dapp.Auth.PendingRequests.Values);

        await _signClientFixture.DisposeAndReset();
    }

    // This test simulates the scenario where the wallet supports all the requested chains and methods
    [Fact]
    public async Task TestAuthenticate_MultipleSignatures_AllChainsAndMethods()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var authParams = GetTestAuthParams();

        var tcs = new TaskCompletionSource();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
            Assert.NotNull(args.Payload.RequestId);

            args.Payload.Populate(authParams.Chains, authParams.Methods!);

            var auths = new List<CacaoObject>();
            foreach (var chainId in args.Payload.Chains)
            {
                var iss = $"did:pkh:{chainId}:{_cryptoWalletFixture.WalletAddress}";
                var cacaoPayload = CacaoPayload.FromAuthPayloadParams(args.Payload, iss);
                var message = cacaoPayload.FormatMessage();
                var signature = await _cryptoWalletFixture.SignMessage(message);
                var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
                var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

                auths.Add(cacao);
            }

            Assert.NotEmpty(auths);

            await wallet.ApproveSessionAuthenticate(args.Payload.RequestId.Value, auths.ToArray());

            tcs.SetResult();
        };

        wallet.SessionProposed += (_, _) => throw new InvalidOperationException("Wallet should not emit session_proposal");

        var authData = await dapp.Authenticate(authParams);
        _ = await wallet.Pair(authData.Uri);

        await tcs.Task;

        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();

        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);

        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = @args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };

        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], "eip155:1");
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));

        // Confirm that all pending proposals and auth requests have been cleared
        Assert.Empty(wallet.Proposal.Values);
        Assert.Empty(wallet.Auth.PendingRequests.Values);
        Assert.Empty(dapp.Proposal.Values);
        Assert.Empty(dapp.Auth.PendingRequests.Values);

        await _signClientFixture.DisposeAndReset();
    }

    // This test simulates the scenario where the wallet supports subset of requested chains and all methods
    [Fact]
    public async Task TestAuthenticate_MultipleSignatures_SomeChainsAllMethods()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var requestedChains = new[]
        {
            "eip155:1",
            "eip155:2"
        };
        var supportedChains = requestedChains.Take(1).ToArray();
        var authParams = GetTestAuthParams(requestedChains);

        var tcs = new TaskCompletionSource();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
            var authPayload = args.Payload;
            Assert.NotNull(authPayload.RequestId);

            authPayload.Populate(supportedChains, authParams.Methods!);
            Assert.Equal(supportedChains, authPayload.Chains);

            var auths = new List<CacaoObject>();
            foreach (var chainId in authPayload.Chains)
            {
                var iss = $"did:pkh:{chainId}:{_cryptoWalletFixture.WalletAddress}";
                var cacaoPayload = CacaoPayload.FromAuthPayloadParams(authPayload, iss);
                var message = cacaoPayload.FormatMessage();
                var signature = await _cryptoWalletFixture.SignMessage(message);
                var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
                var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

                auths.Add(cacao);
            }

            Assert.Equal(supportedChains.Length, auths.Count);

            await wallet.ApproveSessionAuthenticate(authPayload.RequestId.Value, auths.ToArray());

            tcs.SetResult();
        };

        wallet.SessionProposed += (_, _) => throw new InvalidOperationException("Wallet should not emit session_proposal");

        var authData = await dapp.Authenticate(authParams);
        _ = await wallet.Pair(authData.Uri);

        await tcs.Task;

        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();

        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);

        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };

        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], "eip155:1");
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));

        // Confirm that all pending proposals and auth requests have been cleared
        Assert.Empty(wallet.Proposal.Values);
        Assert.Empty(wallet.Auth.PendingRequests.Values);
        Assert.Empty(dapp.Proposal.Values);
        Assert.Empty(dapp.Auth.PendingRequests.Values);

        await _signClientFixture.DisposeAndReset();
    }

    // This test simulates the scenario where the wallet supports subset of requested chains and methods
    [Fact]
    public async Task TestAuthenticate_MultipleSignatures_SomeChainsAndMethods()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var requestedChains = new[]
        {
            "eip155:1",
            "eip155:2"
        };
        var supportedChains = requestedChains.Take(1).ToArray();
        var requestedMethods = new[]
        {
            "personal_sign",
            "eth_chainId",
            "eth_signTypedData_v4"
        };
        var supportedMethods = requestedMethods.Take(1).ToArray();
        var authParams = GetTestAuthParams(requestedChains, requestedMethods);

        var tcs = new TaskCompletionSource();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
            var authPayload = args.Payload;
            Assert.NotNull(authPayload.RequestId);

            authPayload.Populate(supportedChains, supportedMethods);
            Assert.Equal(supportedChains, authPayload.Chains);
            Assert.Equal(supportedMethods, authPayload.Methods);

            var auths = new List<CacaoObject>();
            foreach (var chainId in authPayload.Chains)
            {
                var iss = $"did:pkh:{chainId}:{_cryptoWalletFixture.WalletAddress}";
                var cacaoPayload = CacaoPayload.FromAuthPayloadParams(authPayload, iss);
                var message = cacaoPayload.FormatMessage();
                var signature = await _cryptoWalletFixture.SignMessage(message);
                var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
                var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

                auths.Add(cacao);
            }

            Assert.Equal(supportedChains.Length, auths.Count);

            await wallet.ApproveSessionAuthenticate(authPayload.RequestId.Value, auths.ToArray());

            tcs.SetResult();
        };

        wallet.SessionProposed += (_, _) => throw new InvalidOperationException("Wallet should not emit session_proposal");

        var authData = await dapp.Authenticate(authParams);
        _ = await wallet.Pair(authData.Uri);

        await tcs.Task;

        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();

        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);

        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };

        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], "eip155:1");
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));

        // Confirm that all pending proposals and auth requests have been cleared
        Assert.Empty(wallet.Proposal.Values);
        Assert.Empty(wallet.Auth.PendingRequests.Values);
        Assert.Empty(dapp.Proposal.Values);
        Assert.Empty(dapp.Auth.PendingRequests.Values);

        await _signClientFixture.DisposeAndReset();
    }

    // Should establish normal sign session when URI doesn't specify `wc_sessionAuthenticate` method"
    [Fact]
    public async Task TestAuthenticate_NoWcAuthenticateInUri()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var authParams = GetTestAuthParams();

        var authData = await dapp.Authenticate(authParams);

        Assert.False(string.IsNullOrWhiteSpace(authData.Uri));
        Assert.Contains("wc_sessionAuthenticate", authData.Uri);

        var updatedUri = authData.Uri.Replace("&methods=wc_sessionAuthenticate", string.Empty);

        wallet.SessionProposed += async (_, e) =>
        {
            var approvedNamespaces = new Namespaces(e.Proposal.OptionalNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{_cryptoWalletFixture.WalletAddress}")
                .WithAccount($"eip155:2:{_cryptoWalletFixture.WalletAddress}");

            var approveParams = new ApproveParams
            {
                Id = e.Id,
                Namespaces = approvedNamespaces
            };

            var approveData = await wallet.Approve(approveParams);
            await approveData.Acknowledged();
        };

        _ = await wallet.Pair(updatedUri);

        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();

        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);

        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };

        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], "eip155:1");
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));

        await _signClientFixture.DisposeAndReset();
    }

    // Should establish normal sign session when wallet hasn't subscribed to SessionAuthenticateRequest event
    [Fact]
    public async Task TestAuthenticate_NoWcAuthenticateListeners()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var authParams = GetTestAuthParams();

        var authData = await dapp.Authenticate(authParams);

        Assert.False(string.IsNullOrWhiteSpace(authData.Uri));
        Assert.Contains("wc_sessionAuthenticate", authData.Uri);

        wallet.SessionProposed += async (_, e) =>
        {
            var approvedNamespaces = new Namespaces(e.Proposal.OptionalNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{_cryptoWalletFixture.WalletAddress}")
                .WithAccount($"eip155:2:{_cryptoWalletFixture.WalletAddress}");

            var approveParams = new ApproveParams
            {
                Id = e.Id,
                Namespaces = approvedNamespaces
            };

            var approveData = await wallet.Approve(approveParams);
            await approveData.Acknowledged();
        };

        _ = await wallet.Pair(authData.Uri);

        var dappSession = await authData.Approval;
        var walletSession = wallet.Session.Values.First();

        var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);

        var message = "Hello, .NET!";
        wallet.SessionRequestEvents<PersonalSign, string>().OnRequest += async args =>
        {
            var personalSignMessage = args.Request.Params[0];
            var signature = await _cryptoWalletFixture.SignMessage(personalSignMessage);
            args.Response = signature;
        };

        var signature = await dapp.Request<PersonalSign, string>(dappSession.Topic, [message, _cryptoWalletFixture.WalletAddress], "eip155:1");
        var recoveredAddress = new EthereumMessageSigner().EncodeUTF8AndEcRecover(message, signature);
        Assert.True(recoveredAddress.IsTheSameAddress(_cryptoWalletFixture.WalletAddress));

        await _signClientFixture.DisposeAndReset();
    }
}