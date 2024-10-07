using Newtonsoft.Json;
using Reown.Sign.Models.Cacao;
using Reown.Sign.Models.Engine;
using Reown.TestUtils;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

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


    [Fact]
    public async Task TestAuthenticate()
    {
        await _signClientFixture.DisposeAndReset();
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;
        
        var tcs = new TaskCompletionSource();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
            _testOutputHelper.WriteLine("SessionAuthenticateRequest");
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
            // var pendingProposalsWallet = wallet.Proposal.Values;
            // Assert.Empty(pendingProposalsWallet);

            _testOutputHelper.WriteLine("Populating payload");
            args.Payload.Populate(["eip155:1", "eip155:2"], ["personal_sign", "eth_chainId", "eth_signTypedData_v4"]);
            var iss = $"did:pkh:eip155:1:{_cryptoWalletFixture.WalletAddress}";

            _testOutputHelper.WriteLine("Signing payload");
            var cacaoPayload = CacaoPayload.FromAuthPayloadParams(args.Payload, iss);
            var message = cacaoPayload.FormatMessage();
            var signature = await _cryptoWalletFixture.SignMessage(message);
            var cacaoSignature = new CacaoSignature(CacaoSignatureType.Eip191, signature);
            var cacao = new CacaoObject(CacaoHeader.Caip112, cacaoPayload, cacaoSignature);

            _testOutputHelper.WriteLine("Verifying signature");
            var isSignatureValid = await cacao.VerifySignature(wallet.CoreClient.ProjectId);
            Assert.True(isSignatureValid);

            _testOutputHelper.WriteLine("Approving session authenticate");
            // TODO: DecodeOptionForTopic
            // await wallet.ApproveSessionAuthenticate(args.Payload.RequestId.Value, [cacao]);

            _testOutputHelper.WriteLine("Set tsc to true");
            tcs.SetResult();
            _testOutputHelper.WriteLine("SessionAuthenticateRequest Done");
        };

        wallet.SessionProposed += (_, _) => throw new InvalidOperationException("Wallet should not emit session_proposal");

        var authParams = GetTestAuthParams();

        _testOutputHelper.WriteLine("Authenticating");
        var authData = await dapp.Authenticate(authParams);
        _testOutputHelper.WriteLine(authData.Uri);

        _testOutputHelper.WriteLine("Pairing wallet");
        var proposalStruct = await wallet.Pair(authData.Uri);
        // proposalStruct.ApproveProposal()

        // _testOutputHelper.WriteLine("Waiting for tcs");
        // await tcs.Task;
        //
        // _testOutputHelper.WriteLine("wait for auth approval");
        // var dappSession = await authData.Approval;
        // var walletSession = wallet.Session.Values.First();
        //
        // _testOutputHelper.WriteLine("Test namespaces");
        // var walletNamespacesJson = JsonConvert.SerializeObject(walletSession.Namespaces);
        // var dappSessionNamespacesJson = JsonConvert.SerializeObject(dappSession.Namespaces);
        // Assert.Equal(walletNamespacesJson, dappSessionNamespacesJson);
    }
}