using Reown.Core;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.TestUtils;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

/// <summary>
///     Covers the ordering contract of <see cref="Engine.RequestAsync{T,TR}" />: a response that reaches
///     the SDK before the relay acknowledges our own publish must still complete the call, and a request
///     that is never answered must fail with a descriptive error instead of waiting forever.
/// </summary>
public class RequestAsyncOrderingTests : IAsyncLifetime
{
    private const string TestAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";

    private readonly ITestOutputHelper _testOutputHelper;
    private readonly AckDeferringConnectionBuilder _dappConnectionBuilder = new();

    private SignClient _dapp = null!;
    private SignClient _wallet = null!;

    public RequestAsyncOrderingTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [RpcMethod("ordering_test_method")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99810)]
    public class OrderingTestRequest
    {
        public int a;
        public int b;
    }

    [RpcResponseOptions(Clock.ONE_MINUTE, 99811)]
    public class OrderingTestResponse
    {
        public int result;
    }

    [RpcMethod("ordering_unanswered_method")]
    [RpcRequestOptions(Clock.ONE_MINUTE, 99812)]
    [RpcResponseOptions(Clock.ONE_MINUTE, 99813)]
    public class UnansweredTestRequest
    {
        public int a;
    }

    public async Task InitializeAsync()
    {
        ReownLogger.Instance = new TestOutputHelperLogger(_testOutputHelper);

        _dapp = await SignClient.Init(new SignClientOptions
        {
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Metadata = BuildMetadata("Ordering Dapp"),
            Storage = new InMemoryStorage(),
            ConnectionBuilder = _dappConnectionBuilder
        });

        _wallet = await SignClient.Init(new SignClientOptions
        {
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Metadata = BuildMetadata("Ordering Wallet"),
            Storage = new InMemoryStorage()
        });
    }

    public async Task DisposeAsync()
    {
        _dappConnectionBuilder.Connection?.ReleaseAll();

        if (_dapp?.CoreClient != null)
        {
            await _dapp.CoreClient.Storage.Clear();
            _dapp.Dispose();
        }

        if (_wallet?.CoreClient != null)
        {
            await _wallet.CoreClient.Storage.Clear();
            _wallet.Dispose();
        }

        ReownLogger.Instance = null;
    }

    [Fact]
    [Trait("Category", "integration")]
    public async Task RequestAsync_CompletesWhenTheResponseArrivesBeforeThePublishAcknowledgement()
    {
        var topic = await EstablishSession();

        _wallet.Engine.SessionRequestEvents<OrderingTestRequest, OrderingTestResponse>().OnRequest += requestData =>
        {
            var data = requestData.Request.Params;
            requestData.Response = new OrderingTestResponse
            {
                result = data.a * data.b
            };

            return Task.CompletedTask;
        };

        var connection = _dappConnectionBuilder.Connection;
        Assert.NotNull(connection);

        connection.Arm();

        try
        {
            var response = await _dapp.RequestAsync<OrderingTestRequest, OrderingTestResponse>(
                topic,
                RpcMethodAttribute.MethodForType<OrderingTestRequest>(),
                new OrderingTestRequest
                {
                    a = 6,
                    b = 7
                });

            Assert.Equal(42, response.result);
        }
        finally
        {
            connection.ReleaseAll();
        }

        Assert.True(connection.DeliveredPushBeforeAck,
            "The test did not manage to deliver the response before the publish acknowledgement, so the race was not exercised.");
    }

    [Fact]
    [Trait("Category", "integration")]
    public async Task RequestAsync_WithNoResponder_FailsWithSessionRequestExpired()
    {
        var topic = await EstablishSession();

        var exception = await Assert.ThrowsAsync<ReownNetworkException>(() =>
            _dapp.RequestAsync<UnansweredTestRequest, bool>(
                topic,
                RpcMethodAttribute.MethodForType<UnansweredTestRequest>(),
                new UnansweredTestRequest
                {
                    a = 1
                },
                expiry: Clock.THIRTY_SECONDS));

        Assert.Equal(ErrorType.SESSION_REQUEST_EXPIRED, exception.CodeType);
        Assert.Contains(RpcMethodAttribute.MethodForType<UnansweredTestRequest>(), exception.Message);
    }

    private async Task<string> EstablishSession()
    {
        var connectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods =
                        [
                            RpcMethodAttribute.MethodForType<OrderingTestRequest>(),
                            RpcMethodAttribute.MethodForType<UnansweredTestRequest>()
                        ],
                        Chains = ["eip155:1"],
                        Events = ["chainChanged", "accountsChanged"]
                    }
                }
            }
        };

        var settled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _wallet.SessionProposed += async (_, @event) =>
        {
            try
            {
                var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
                approvedNamespaces["eip155"].WithAccount($"eip155:1:{TestAddress}");

                var approveData = await _wallet.Approve(new ApproveParams
                {
                    Id = @event.Id,
                    Namespaces = approvedNamespaces
                });

                await approveData.Acknowledged();

                settled.TrySetResult(true);
            }
            catch (Exception e)
            {
                settled.TrySetException(e);
            }
        };

        var connectData = await _dapp.Connect(connectOptions);
        _ = await _wallet.Pair(connectData.Uri);

        await settled.Task;
        var session = await connectData.Approval;

        return session.Topic;
    }

    private static Metadata BuildMetadata(string name)
    {
        return new Metadata
        {
            Description = name,
            Icons =
            [
                "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/reown-avatar-positive.png"
            ],
            Name = name,
            Url = "https://reown.com"
        };
    }
}
