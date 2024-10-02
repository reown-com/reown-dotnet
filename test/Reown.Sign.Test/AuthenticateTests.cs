using Reown.Sign.Models.Engine;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

public class AuthenticateTests : IClassFixture<SignClientFixture>
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly SignClientFixture _signClientFixture;

    public AuthenticateTests(SignClientFixture signClientFixture, ITestOutputHelper testOutputHelper)
    {
        _signClientFixture = signClientFixture;
        _testOutputHelper = testOutputHelper;
    }

    private static AuthParams GetTestAuthParams()
    {
        return new AuthParams(
            [
                "eip155:1"
            ],
            "service.invalid",
            "32891756",
            "https://service.invalid/login",
            null,
            null,
            "I accept the ServiceOrg Terms of Service: https://service.invalid/tos",
            null,
            [
                "ipfs://bafybeiemxf5abjwjbikoz4mc3a3dla6ual3jsgpdr4cjr3oz3evfyavhwq/",
                "https://example.com/my-web2-claim.json"
            ],
            [
                "personal_sign",
                "eth_sendTransaction"
            ]
        );
    }


    [Fact]
    public async Task TestAuthenticate()
    {
        await _signClientFixture.WaitForClientsReady();

        var dapp = _signClientFixture.ClientA;
        var wallet = _signClientFixture.ClientB;

        var tcs = new TaskCompletionSource<bool>();
        wallet.SessionAuthenticateRequest += async (sender, args) =>
        {
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

            // Validate that the wallet doesn't have any pending proposals
            var pendingProposalsWallet = wallet.Proposal.Values;
            Assert.Empty(pendingProposalsWallet);

            args.Payload.Populate([
                "eip155:1"
            ], [
                "personal_sign",
                "eth_sendTransaction"
            ]);
            
            _testOutputHelper.WriteLine("SessionAuthenticateRequest");
            tcs.SetResult(true);
        };

        var authParams = GetTestAuthParams();

        var authData = await dapp.Authenticate(authParams);
        _testOutputHelper.WriteLine(authData.Uri);

        await wallet.Pair(authData.Uri);

        await tcs.Task;
    }
}