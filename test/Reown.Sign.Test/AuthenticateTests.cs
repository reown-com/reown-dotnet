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