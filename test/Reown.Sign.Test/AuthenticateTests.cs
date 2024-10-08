using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Nethereum.Util;
using Newtonsoft.Json;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
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
    }

    private static AuthParams GetTestAuthParams()
    {
        return new AuthParams(
            ["eip155:1", "eip155:2"],
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
    public async Task TestAuthenticate_SingleSignature()
    {
        await _signClientFixture.DisposeAndReset();
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;
        
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

            // TODO: Validate that the wallet doesn't have any pending proposals
            var pendingProposalsWallet = wallet.Proposal.Values;
            // Assert.Empty(pendingProposalsWallet);

            args.Payload.Populate(["eip155:1", "eip155:2"], ["personal_sign", "eth_chainId", "eth_signTypedData_v4"]);
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

        var authParams = GetTestAuthParams();
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
    }
}