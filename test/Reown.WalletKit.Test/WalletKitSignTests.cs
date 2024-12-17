using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.Core.Common.Logging;
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
        ReownLogger.Instance = new TestOutputHelperLogger(testOutputHelper);
        _fixture = fixture;
        _cryptoWalletFixture = cryptoWalletFixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestShouldApproveSessionProposal()
    {
        _testOutputHelper.WriteLine("[TestShouldApproveSessionProposal] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestShouldApproveSessionProposal] Clients ready");

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _fixture.WalletClient.ApproveSession(id, TestNamespaces);
            _testOutputHelper.WriteLine("[TestShouldApproveSessionProposal] Session approved");

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;
        _testOutputHelper.WriteLine("[TestShouldApproveSessionProposal] DappClient connected, URI obtained");

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );
        _testOutputHelper.WriteLine("[TestShouldApproveSessionProposal] All tasks completed successfully");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestShouldRejectSessionProposal()
    {
        _testOutputHelper.WriteLine("[TestShouldRejectSessionProposal] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestShouldRejectSessionProposal] Clients ready");

        var rejectionError = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var proposal = @event.Proposal;

            var id = @event.Id;
            Assert.Equal(TestRequiredNamespaces, proposal.RequiredNamespaces);

            await _fixture.WalletClient.RejectSession(id, rejectionError);
            _testOutputHelper.WriteLine("[TestShouldRejectSessionProposal] Session rejected");
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
        _testOutputHelper.WriteLine("[TestShouldRejectSessionProposal] DappClient connected, URI obtained");

        await Task.WhenAll(
            task1.Task,
            _fixture.WalletClient.Pair(uriString),
            CheckSessionReject()
        );
        _testOutputHelper.WriteLine("[TestShouldRejectSessionProposal] All tasks completed successfully");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestUpdateSession()
    {
        _testOutputHelper.WriteLine("[TestUpdateSession] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestUpdateSession] Clients ready");

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _fixture.WalletClient.ApproveSession(id, TestNamespaces);
            _testOutputHelper.WriteLine("[TestUpdateSession] Session approved");

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;
        _testOutputHelper.WriteLine("[TestUpdateSession] DappClient connected, URI obtained");

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );
        _testOutputHelper.WriteLine("[TestUpdateSession] Initial session setup completed");

        Assert.NotEqual(TestNamespaces, TestUpdatedNamespaces);
        _testOutputHelper.WriteLine("[TestUpdateSession] Namespaces validation passed");

        var task2 = new TaskCompletionSource<bool>();
        _fixture.DappClient.SessionUpdateRequest += (sender, @event) =>
        {
            var param = @event.Params;
            _testOutputHelper.WriteLine("[TestUpdateSession] Received session update request");
            Assert.Equal(TestUpdatedNamespaces, param.Namespaces);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _fixture.WalletClient.UpdateSession(session.Topic, TestUpdatedNamespaces)
        );
        _testOutputHelper.WriteLine("[TestUpdateSession] Session update completed successfully");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestExtendSession()
    {
        _testOutputHelper.WriteLine("[TestExtendSession] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestExtendSession] Clients ready");

        var task1 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _fixture.WalletClient.ApproveSession(id, TestNamespaces);
            _testOutputHelper.WriteLine("[TestExtendSession] Session approved");

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        var connectData = await _fixture.DappClient.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;
        _testOutputHelper.WriteLine("[TestExtendSession] DappClient connected, URI obtained");

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _fixture.WalletClient.Pair(uriString)
        );
        _testOutputHelper.WriteLine("[TestExtendSession] Initial session setup completed");

        var prevExpiry = session.Expiry;
        var topic = session.Topic;
        _testOutputHelper.WriteLine($"[TestExtendSession] Previous expiry: {prevExpiry}");

        // TODO Figure out if we need fake timers?
        await Task.Delay(5000);
        _testOutputHelper.WriteLine("[TestExtendSession] Waited 5 seconds before extending session");

        await _fixture.WalletClient.ExtendSession(topic);
        _testOutputHelper.WriteLine("[TestExtendSession] Session extension request sent");

        var updatedExpiry = _fixture.WalletClient.Engine.SignClient.Session.Get(topic).Expiry;
        _testOutputHelper.WriteLine($"[TestExtendSession] Updated expiry: {updatedExpiry}");

        Assert.True(updatedExpiry > prevExpiry);
        _testOutputHelper.WriteLine("[TestExtendSession] Expiry validation passed");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestRespondToSessionRequest()
    {
        _testOutputHelper.WriteLine("[TestRespondToSessionRequest] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestRespondToSessionRequest] Clients ready");

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
            _testOutputHelper.WriteLine("[TestRespondToSessionRequest] Session approved");

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
            _testOutputHelper.WriteLine("[TestRespondToSessionRequest] Transaction signed");

            var signature = ((AccountSignerTransactionManager)_cryptoWalletFixture.CryptoWallet
                    .GetAccount(0).TransactionManager)
                .SignTransaction(signTransaction);

            args.Response = signature;
            task2.TrySetResult(true);

            return Task.CompletedTask;
        };

        async Task SendRequest()
        {
            _testOutputHelper.WriteLine("[TestRespondToSessionRequest] Sending transaction request");
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
            _testOutputHelper.WriteLine("[TestRespondToSessionRequest] Transaction request sent");

            Assert.False(string.IsNullOrWhiteSpace(result));
            _testOutputHelper.WriteLine("[TestRespondToSessionRequest] Transaction signature validation passed");
        }

        await Task.WhenAll(
            task2.Task,
            SendRequest()
        );
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestWalletDisconnectFromSession()
    {
        _testOutputHelper.WriteLine("[TestWalletDisconnectFromSession] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestWalletDisconnectFromSession] Clients ready");

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
            _testOutputHelper.WriteLine("[TestWalletDisconnectFromSession] Session approved");

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
        _testOutputHelper.WriteLine($"[TestWalletDisconnectFromSession] Disconnecting with reason: {reason.Message}");

        var task2 = new TaskCompletionSource<bool>();
        _fixture.DappClient.SessionDeleted += (sender, @event) =>
        {
            _testOutputHelper.WriteLine("[TestWalletDisconnectFromSession] Session deleted event received");
            Assert.Equal(session.Topic, @event.Topic);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _fixture.WalletClient.DisconnectSession(session.Topic, reason)
        );
        _testOutputHelper.WriteLine("[TestWalletDisconnectFromSession] Session disconnected");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestDappDisconnectFromSession()
    {
        _testOutputHelper.WriteLine("[TestDappDisconnectFromSession] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestDappDisconnectFromSession] Clients ready");

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
            _testOutputHelper.WriteLine("[TestDappDisconnectFromSession] Session approved");

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
        _testOutputHelper.WriteLine($"[TestDappDisconnectFromSession] Disconnecting with reason: {reason.Message}");

        var task2 = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionDeleted += (sender, @event) =>
        {
            _testOutputHelper.WriteLine("[TestDappDisconnectFromSession] Session deleted event received");
            Assert.Equal(session.Topic, @event.Topic);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _fixture.DappClient.Disconnect(session.Topic, reason)
        );
        _testOutputHelper.WriteLine("[TestDappDisconnectFromSession] Session disconnected");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestEmitSessionEvent()
    {
        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Clients ready");

        var pairingTask = new TaskCompletionSource<bool>();
        _fixture.WalletClient.SessionProposed += async (sender, @event) =>
        {
            var proposals = _fixture.WalletClient.PendingSessionProposals;
            Assert.NotNull(proposals);
            Assert.Single(proposals);
            Assert.Equal(TestRequiredNamespaces, proposals.Values.ToArray()[0].RequiredNamespaces);

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
            _testOutputHelper.WriteLine("[TestEmitSessionEvent] Session approved");

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
            _testOutputHelper.WriteLine("[TestEmitSessionEvent] Reference type event received");
            var eventData = data.Event.Data.ToObject<TestDataObject>();

            Assert.Equal(referenceTypeEventData.Name, data.Event.Name);
            Assert.Equal(referenceTypeEventData.Data.Hello, eventData.Hello);
            _testOutputHelper.WriteLine("[TestEmitSessionEvent] Reference type event validation passed");

            referenceHandlingTask.TrySetResult(true);
        }

        void ValueTypeEventHandler(object _, SessionEvent<JToken> eventData)
        {
            _testOutputHelper.WriteLine("[TestEmitSessionEvent] Value type event received");
            var data = eventData.Event.Data.Value<long>();

            Assert.Equal(valueTypeEventData.Name, eventData.Event.Name);
            Assert.Equal(valueTypeEventData.Data, data);
            _testOutputHelper.WriteLine("[TestEmitSessionEvent] Value type event validation passed");

            valueHandlingTask.TrySetResult(true);
        }

        _fixture.DappClient.SubscribeToSessionEvent(referenceTypeEventData.Name, ReferenceTypeEventHandler);
        _fixture.DappClient.SubscribeToSessionEvent(valueTypeEventData.Name, ValueTypeEventHandler);
        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Event handlers subscribed");

        await Task.WhenAll(
            referenceHandlingTask.Task,
            valueHandlingTask.Task,
            _fixture.WalletClient.EmitSessionEvent(session.Topic, referenceTypeEventData, TestRequiredNamespaces["eip155"].Chains[0]),
            _fixture.WalletClient.EmitSessionEvent(session.Topic, valueTypeEventData, TestRequiredNamespaces["eip155"].Chains[0])
        );
        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Events emitted");

        Assert.True(_fixture.DappClient.TryUnsubscribeFromSessionEvent(referenceTypeEventData.Name, ReferenceTypeEventHandler));
        Assert.True(_fixture.DappClient.TryUnsubscribeFromSessionEvent(valueTypeEventData.Name, ValueTypeEventHandler));

        // Test invalid chains
        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Testing invalid chain format");
        await Assert.ThrowsAsync<FormatException>(() => _fixture.WalletClient.EmitSessionEvent(session.Topic, valueTypeEventData, "invalid chain"));
        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Invalid chain format test passed");

        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Testing invalid chain namespace");
        await Assert.ThrowsAsync<NamespacesException>(() => _fixture.WalletClient.EmitSessionEvent(session.Topic, valueTypeEventData, "123:321"));
        _testOutputHelper.WriteLine("[TestEmitSessionEvent] Invalid chain namespace test passed");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestGetActiveSessions()
    {
        _testOutputHelper.WriteLine("[TestGetActiveSessions] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestGetActiveSessions] Clients ready");

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
            _testOutputHelper.WriteLine("[TestGetActiveSessions] Session approved");

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
        _testOutputHelper.WriteLine($"[TestGetActiveSessions] Retrieved {sessions.Count} active sessions");
        Assert.NotNull(sessions);
        Assert.Single(sessions);
        _testOutputHelper.WriteLine($"[TestGetActiveSessions] Session topic: {sessions.Values.ToArray()[0].Topic}");
        Assert.Equal(session.Topic, sessions.Values.ToArray()[0].Topic);
        _testOutputHelper.WriteLine("[TestGetActiveSessions] Session validation passed");
    }

    [Fact(Timeout = 300_000)] [Trait("Category", "integration")]
    public async Task TestGetPendingSessionRequests()
    {
        _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Starting");
        await _fixture.DisposeAndReset();
        await _fixture.WaitForClientsReady();
        _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Clients ready");

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
            _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Session approved");

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
            _testOutputHelper.WriteLine($"[TestGetPendingSessionRequests] Processing request ID: {id}");

            // Perform unsafe cast, all pending requests are stored as object type
            var signTransaction = ((EthSignTransaction)request.Parameters.Request.Params)[0];
            _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Transaction parameters extracted");

            Assert.Equal(args.Request.Id, id);
            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Request validation passed");

            var signature = ((AccountSignerTransactionManager)_cryptoWalletFixture.CryptoWallet
                    .GetAccount(0).TransactionManager)
                .SignTransaction(signTransaction);
            _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Transaction signed");

            args.Response = signature;
            task2.TrySetResult(true);
            return Task.CompletedTask;
        };

        async Task SendRequest()
        {
            _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Sending transaction request");
            var result = await _fixture.DappClient.Request<EthSignTransaction, string>(session.Topic,
                requestParams, TestEthereumChain);
            _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Transaction request sent");

            Assert.False(string.IsNullOrWhiteSpace(result));
            _testOutputHelper.WriteLine("[TestGetPendingSessionRequests] Transaction signature validation passed");
        }

        await Task.WhenAll(
            task2.Task,
            SendRequest()
        );
    }
}