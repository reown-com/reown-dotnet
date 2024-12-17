using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Verify;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;
using Reown.TestUtils;
using Xunit;
using Xunit.Abstractions;

namespace Reown.WalletKit.Test;

public class WalletKitSignTests :
    IClassFixture<WalletKitSignClientFixture>,
    IClassFixture<CryptoWalletFixture>
{
    [RpcMethod("eth_signTransaction")] [RpcRequestOptions(Clock.ONE_MINUTE, 99997)] [RpcResponseOptions(Clock.ONE_MINUTE, 99996)]
    public class EthSignTransaction : List<TransactionInput>
    {
    }

    public class TestDataObject
    {
        [JsonProperty("hello")]
        public string Hello;
    }

    private static readonly string TestEthereumAddress = "0x3c582121909DE92Dc89A36898633C1aE4790382b";
    private static readonly string TestEthereumChain = "eip155:1";
    private static readonly string TestArbitrumChain = "eip155:42161";
    private static readonly string TestAvalancheChain = "eip155:43114";

    private static readonly string[] TestAccounts = new[]
    {
        $"{TestEthereumChain}:{TestEthereumAddress}",
        $"{TestArbitrumChain}:{TestEthereumAddress}",
        $"{TestAvalancheChain}:{TestEthereumAddress}"
    };

    private static readonly string[] TestEvents = new[]
    {
        "chainChanged",
        "accountsChanged",
        "valueTypeEvent",
        "referenceTypeEvent"
    };

    private static readonly RequiredNamespaces TestRequiredNamespaces = new()
    {
        {
            "eip155", new ProposedNamespace
            {
                Chains = new[]
                {
                    "eip155:1"
                },
                Methods = new[]
                {
                    "eth_signTransaction"
                },
                Events = TestEvents
            }
        }
    };

    private static readonly Namespaces TestUpdatedNamespaces = new()
    {
        {
            "eip155", new Namespace
            {
                Methods = new[]
                {
                    "eth_signTransaction",
                    "eth_sendTransaction",
                    "personal_sign",
                    "eth_signTypedData"
                },
                Accounts = TestAccounts,
                Events = TestEvents,
                Chains = new[]
                {
                    TestEthereumChain
                }
            }
        }
    };

    private static readonly Namespace TestNamespace = new()
    {
        Methods = ["eth_signTransaction"],
        Accounts = [TestAccounts[0]],
        Events = TestEvents,
        Chains = [TestEthereumChain]
    };

    private static readonly Namespaces TestNamespaces = new()
    {
        { "eip155", TestNamespace }
    };

    private static readonly ConnectOptions TestConnectOptions = new ConnectOptions()
        .UseRequireNamespaces(TestRequiredNamespaces);

    private readonly WalletKitSignClientFixture _fixture;
    private readonly CryptoWalletFixture _cryptoWalletFixture;
    private readonly ITestOutputHelper _testOutputHelper;
    private string uriString;
    private Task<Session> sessionApproval;
    private Session session;

    public string WalletAddress
    {
        get => _cryptoWalletFixture.WalletAddress;
    }

    public string Iss
    {
        get => _cryptoWalletFixture.Iss;
    }

    public WalletKitSignTests(
        WalletKitSignClientFixture fixture,
        CryptoWalletFixture cryptoWalletFixture,
        ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _cryptoWalletFixture = cryptoWalletFixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestShouldApproveSessionProposal()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _fixture.WalletClient.ApproveSession(id, TestNamespaces);

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestShouldRejectSessionProposal()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var rejectionError = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var proposal = @event.Proposal;

            var id = @event.Id;
            Assert.Equal(TestRequiredNamespaces, proposal.RequiredNamespaces);

            await _fixture.WalletClient.RejectSession(id, rejectionError);
            task1.TrySetResult(true);
        };

        async Task CheckSessionReject()
        {
            try
            {
                await sessionApproval;
            }
            catch (ReownNetworkException e)
            {
                Assert.Equal(rejectionError.Code, e.Code);
                Assert.Equal(rejectionError.Message, e.Message);
                return;
            }

            Assert.Fail("Session approval task did not throw exception, expected rejection");
        }

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            _fixture.WalletClient.Pair(uriString),
            CheckSessionReject()
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestUpdateSession()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _fixture.WalletClient.ApproveSession(id, TestNamespaces);

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        Assert.NotEqual(TestNamespaces, TestUpdatedNamespaces);

        var task2 = new TaskCompletionSource<bool>();
        _fixture.DappClient.SessionUpdateRequest += (sender, @event) =>
        {
            var param = @event.Params;
            Assert.Equal(TestUpdatedNamespaces, param.Namespaces);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _fixture.WalletClient.UpdateSession(session.Topic, TestUpdatedNamespaces)
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestExtendSession()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _fixture.WalletClient.ApproveSession(id, TestNamespaces);

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        var prevExpiry = session.Expiry;
        var topic = session.Topic;

        // TODO Figure out if we need fake timers?
        await Task.Delay(5000);

        await _fixture.WalletClient.ExtendSession(topic);

        var updatedExpiry = _fixture.WalletClient.Engine.SignClient.Session.Get(topic).Expiry;

        Assert.True(updatedExpiry > prevExpiry);
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestRespondToSessionRequest()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _fixture.WalletClient.ApproveSession(id, new Namespaces
            {
                {
                    "eip155", new Namespace
                    {
                        Methods = TestNamespace.Methods,
                        Events = TestNamespace.Events,
                        Accounts = new[]
                        {
                            $"{TestEthereumChain}:{WalletAddress}"
                        },
                        Chains = new[]
                        {
                            TestEthereumChain
                        }
                    }
                }
            });

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        var task2 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.Engine.SignClient.Engine.SessionRequestEvents<EthSignTransaction, string>()
            .OnRequest += args =>
        {
            var id = args.Request.Id;
            var @params = args.Request;
            var verifyContext = args.VerifiedContext;
            var signTransaction = @params.Params[0];

            Assert.Equal(Validation.Unknown, verifyContext.Validation);

            var signature = ((AccountSignerTransactionManager)_cryptoWalletFixture.CryptoWallet
                    .GetAccount(0).TransactionManager)
                .SignTransaction(signTransaction);

            args.Response = signature;
            task2.TrySetResult(true);

            return Task.CompletedTask;
        };

        async Task SendRequest()
        {
            var result = await _fixture.DappClient.Request<EthSignTransaction, string>(session.Topic,
            [
                new TransactionInput
                {
                    From = WalletAddress,
                    To = WalletAddress,
                    Data = "0x",
                    Nonce = new HexBigInteger("0x1"),
                    GasPrice = new HexBigInteger("0x020a7ac094"),
                    Gas = new HexBigInteger("0x5208"),
                    Value = new HexBigInteger("0x00")
                }
            ], TestEthereumChain);

            Assert.False(string.IsNullOrWhiteSpace(result));
        }

        await Task.WhenAll(
            task2.Task,
            SendRequest()
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestWalletDisconnectFromSession()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _fixture.WalletClient.ApproveSession(id, new Namespaces
            {
                {
                    "eip155", new Namespace
                    {
                        Methods = TestNamespace.Methods,
                        Events = TestNamespace.Events,
                        Accounts = new[]
                        {
                            $"{TestEthereumChain}:{WalletAddress}"
                        },
                        Chains = new[]
                        {
                            TestEthereumChain
                        }
                    }
                }
            });

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        var reason = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        var task2 = new TaskCompletionSource<bool>();
        _fixture.DappClient.SessionDeleted += (sender, @event) =>
        {
            Assert.Equal(session.Topic, @event.Topic);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _fixture.WalletClient.DisconnectSession(session.Topic, reason)
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestDappDisconnectFromSession()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _fixture.WalletClient.ApproveSession(id, new Namespaces
            {
                {
                    "eip155", new Namespace
                    {
                        Methods = TestNamespace.Methods,
                        Events = TestNamespace.Events,
                        Accounts = new[]
                        {
                            $"{TestEthereumChain}:{WalletAddress}"
                        },
                        Chains = new[]
                        {
                            TestEthereumChain
                        }
                    }
                }
            });

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        var reason = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        var task2 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionDeleted += (sender, @event) =>
        {
            Assert.Equal(session.Topic, @event.Topic);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _fixture.DappClient.Disconnect(session.Topic, reason)
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestEmitSessionEvent()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var pairingTask = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;

            session = await _fixture.WalletClient.ApproveSession(id, new Namespaces
            {
                {
                    "eip155", new Namespace
                    {
                        Methods = TestNamespace.Methods,
                        Events = TestNamespace.Events,
                        Accounts = [$"{TestEthereumChain}:{WalletAddress}"],
                        Chains = [TestEthereumChain]
                    }
                }
            });

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            pairingTask.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            pairingTask.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        var referenceHandlingTask = new TaskCompletionSource<bool>();
        var valueHandlingTask = new TaskCompletionSource<bool>();

        var referenceTypeEventData = new EventData<TestDataObject>
        {
            Name = "referenceTypeEvent",
            Data = new TestDataObject
            {
                Hello = "World"
            }
        };

        var valueTypeEventData = new EventData<long>
        {
            Name = "valueTypeEvent",
            Data = 10
        };

        void ReferenceTypeEventHandler(object _, SessionEvent<JToken> data)
        {
            var eventData = data.Event.Data.ToObject<TestDataObject>();

            Assert.Equal(referenceTypeEventData.Name, data.Event.Name);
            Assert.Equal(referenceTypeEventData.Data.Hello, eventData.Hello);

            referenceHandlingTask.TrySetResult(true);
        }

        void ValueTypeEventHandler(object _, SessionEvent<JToken> eventData)
        {
            var data = eventData.Event.Data.Value<long>();

            Assert.Equal(valueTypeEventData.Name, eventData.Event.Name);
            Assert.Equal(valueTypeEventData.Data, data);

            valueHandlingTask.TrySetResult(true);
        }

        _fixture.DappClient.SubscribeToSessionEvent(referenceTypeEventData.Name, ReferenceTypeEventHandler);
        _fixture.DappClient.SubscribeToSessionEvent(valueTypeEventData.Name, ValueTypeEventHandler);

        await Task.WhenAll(
            referenceHandlingTask.Task,
            valueHandlingTask.Task,
            _fixture.WalletClient.EmitSessionEvent(session.Topic, referenceTypeEventData, TestRequiredNamespaces["eip155"].Chains[0]),
            _fixture.WalletClient.EmitSessionEvent(session.Topic, valueTypeEventData, TestRequiredNamespaces["eip155"].Chains[0])
        );

        Assert.True(_fixture.DappClient.TryUnsubscribeFromSessionEvent(referenceTypeEventData.Name, ReferenceTypeEventHandler));
        Assert.True(_fixture.DappClient.TryUnsubscribeFromSessionEvent(valueTypeEventData.Name, ValueTypeEventHandler));

        // Test invalid chains
        await Assert.ThrowsAsync<FormatException>(() => _fixture.WalletClient.EmitSessionEvent(session.Topic, valueTypeEventData, "invalid chain"));
        await Assert.ThrowsAsync<NamespacesException>(() => _fixture.WalletClient.EmitSessionEvent(session.Topic, valueTypeEventData, "123:321"));
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestGetActiveSessions()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _fixture.WalletClient.ApproveSession(id,
                new Namespaces
                {
                    {
                        "eip155", new Namespace
                        {
                            Methods = TestNamespace.Methods,
                            Events = TestNamespace.Events,
                            Accounts = new[]
                            {
                                $"{TestEthereumChain}:{WalletAddress}"
                            },
                            Chains = new[]
                            {
                                TestEthereumChain
                            }
                        }
                    }
                });

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        var sessions = _fixture.WalletClient.ActiveSessions;
        Assert.NotNull(sessions);
        Assert.Single(sessions);
        Assert.Equal(session.Topic, sessions.Values.ToArray()[0].Topic);
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestGetPendingSessionProposals()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += (sender, @event) =>
        {
            var proposals = _fixture.WalletClient.PendingSessionProposals;
            Assert.NotNull(proposals);
            Assert.Single(proposals);
            Assert.Equal(TestRequiredNamespaces, proposals.Values.ToArray()[0].RequiredNamespaces);
            task1.TrySetResult(true);
        };

        await Task.WhenAll(
            task1.Task,
            _fixture.WalletClient.Pair(uriString)
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestGetPendingSessionRequests()
    {
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _fixture.WalletClient.ApproveSession(id,
                new Namespaces
                {
                    {
                        "eip155", new Namespace
                        {
                            Methods = TestNamespace.Methods,
                            Events = TestNamespace.Events,
                            Accounts = new[]
                            {
                                $"{TestEthereumChain}:{WalletAddress}"
                            },
                            Chains = new[]
                            {
                                TestEthereumChain
                            }
                        }
                    }
                });

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );

        var requestParams = new EthSignTransaction
        {
            new TransactionInput
            {
                From = WalletAddress,
                To = WalletAddress,
                Data = "0x",
                Nonce = new HexBigInteger("0x1"),
                GasPrice = new HexBigInteger("0x020a7ac094"),
                Gas = new HexBigInteger("0x5208"),
                Value = new HexBigInteger("0x00")
            }
        };

        var task2 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.Engine.SignClient.Engine.SessionRequestEvents<EthSignTransaction, string>()
            .OnRequest += args =>
        {
            // Get the pending session request, since that's what we're testing
            var pendingRequests = _fixture.WalletClient.PendingSessionRequests;
            var request = pendingRequests[0];

            var id = request.Id;
            var verifyContext = args.VerifiedContext;

            // Perform unsafe cast, all pending requests are stored as object type
            var signTransaction = ((EthSignTransaction)request.Parameters.Request.Params)[0];

            Assert.Equal(args.Request.Id, id);
            Assert.Equal(Validation.Unknown, verifyContext.Validation);

            var signature = ((AccountSignerTransactionManager)_cryptoWalletFixture.CryptoWallet
                    .GetAccount(0).TransactionManager)
                .SignTransaction(signTransaction);

            args.Response = signature;
            task2.TrySetResult(true);
            return Task.CompletedTask;
        };

        async Task SendRequest()
        {
            var result = await _fixture.DappClient.Request<EthSignTransaction, string>(session.Topic,
                requestParams, TestEthereumChain);

            Assert.False(string.IsNullOrWhiteSpace(result));
        }

        await Task.WhenAll(
            task2.Task,
            SendRequest()
        );
    }
}