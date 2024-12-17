using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.Core;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Models;
using Reown.Core.Models.Verify;
using Reown.Core.Network.Models;
using Reown.Core.Network.Websocket;
using Reown.Core.Storage;
using Reown.Sign;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Models.Engine.Methods;
using Reown.TestUtils;
using Xunit;
using Xunit.Abstractions;
using Metadata = Reown.Core.Metadata;

namespace Reown.WalletKit.Test;

public class SignClientTests : IClassFixture<CryptoWalletFixture>, IAsyncLifetime
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

    private readonly CryptoWalletFixture _cryptoWalletFixture;
    private readonly ITestOutputHelper _testOutputHelper;
    private CoreClient _coreClient;
    private SignClient _dapp;
    private WalletKitClient _wallet;
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

    private static readonly string[] second = new[]
    {
        "chainChanged2"
    };

    public SignClientTests(CryptoWalletFixture cryptoWalletFixture, ITestOutputHelper testOutputHelper)
    {
        _cryptoWalletFixture = cryptoWalletFixture;
        _testOutputHelper = testOutputHelper;
    }

    public async Task InitializeAsync()
    {
        _coreClient = new CoreClient(new CoreOptions
        {
            ConnectionBuilder = new WebsocketConnectionBuilder(),
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Name = $"wallet-csharp-test-{Guid.NewGuid().ToString()}",
            Storage = new InMemoryStorage()
        });
        _dapp = await SignClient.Init(new SignClientOptions
        {
            ProjectId = TestValues.TestProjectId,
            Name = $"dapp-csharp-test-{Guid.NewGuid().ToString()}",
            RelayUrl = TestValues.TestRelayUrl,
            Metadata = new Metadata
            {
                Description = "An example dapp to showcase WalletKit",
                Icons = ["https://walletconnect.com/meta/favicon.ico"],
                Name = $"dapp-csharp-test-{Guid.NewGuid().ToString()}",
                Url = "https://walletconnect.com"
            },
            Storage = new InMemoryStorage()
        });
        var connectData = await _dapp.Connect(TestConnectOptions);
        uriString = connectData.Uri ?? "";
        sessionApproval = connectData.Approval;

        _wallet = await WalletKitClient.Init(_coreClient, new Metadata
            {
                Description = "An example wallet to showcase WalletKit",
                Icons = new[]
                {
                    "https://walletconnect.com/meta/favicon.ico"
                },
                Name = $"wallet-csharp-test-{Guid.NewGuid().ToString()}",
                Url = "https://walletconnect.com"
            }, $"wallet-csharp-test-{Guid.NewGuid().ToString()}");

        Assert.NotNull(_wallet);
        Assert.NotNull(_dapp);
        Assert.NotNull(_coreClient);
        Assert.Null(_wallet.Metadata.Redirect);
    }

    public async Task DisposeAsync()
    {
        if (_coreClient.Relayer.Connected)
        {
            await _coreClient.Relayer.TransportClose();
        }
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestShouldApproveSessionProposal()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _wallet.ApproveSession(id, TestNamespaces);

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
        );
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestShouldRejectSessionProposal()
    {
        var rejectionError = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var proposal = @event.Proposal;

            var id = @event.Id;
            Assert.Equal(TestRequiredNamespaces, proposal.RequiredNamespaces);

            await _wallet.RejectSession(id, rejectionError);
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

        await Task.WhenAll(
            task1.Task,
            _wallet.Pair(uriString),
            CheckSessionReject()
        );
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestUpdateSession()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _wallet.ApproveSession(id, TestNamespaces);

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
        );

        Assert.NotEqual(TestNamespaces, TestUpdatedNamespaces);

        var task2 = new TaskCompletionSource<bool>();
        _dapp.SessionUpdateRequest += (sender, @event) =>
        {
            var param = @event.Params;
            Assert.Equal(TestUpdatedNamespaces, param.Namespaces);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _wallet.UpdateSession(session.Topic, TestUpdatedNamespaces)
        );
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestExtendSession()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            Assert.Equal(Validation.Unknown, verifyContext.Validation);
            session = await _wallet.ApproveSession(id, TestNamespaces);

            Assert.Equal(proposal.RequiredNamespaces, TestRequiredNamespaces);
            task1.TrySetResult(true);
        };

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
        );

        var prevExpiry = session.Expiry;
        var topic = session.Topic;

        // TODO Figure out if we need fake timers?
        await Task.Delay(5000);

        await _wallet.ExtendSession(topic);

        var updatedExpiry = _wallet.Engine.SignClient.Session.Get(topic).Expiry;

        Assert.True(updatedExpiry > prevExpiry);
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestRespondToSessionRequest()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _wallet.ApproveSession(id, new Namespaces
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

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
        );

        var task2 = new TaskCompletionSource<bool>();
        _wallet.Engine.SignClient.Engine.SessionRequestEvents<EthSignTransaction, string>()
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
            var result = await _dapp.Request<EthSignTransaction, string>(session.Topic,
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

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestWalletDisconnectFromSession()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _wallet.ApproveSession(id, new Namespaces
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

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
        );

        var reason = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        var task2 = new TaskCompletionSource<bool>();
        _dapp.SessionDeleted += (sender, @event) =>
        {
            Assert.Equal(session.Topic, @event.Topic);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _wallet.DisconnectSession(session.Topic, reason)
        );
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestDappDisconnectFromSession()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _wallet.ApproveSession(id, new Namespaces
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

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
        );

        var reason = Error.FromErrorType(ErrorType.USER_DISCONNECTED);

        var task2 = new TaskCompletionSource<bool>();
        _wallet.SessionDeleted += (sender, @event) =>
        {
            Assert.Equal(session.Topic, @event.Topic);
            task2.TrySetResult(true);
        };

        await Task.WhenAll(
            task2.Task,
            _dapp.Disconnect(session.Topic, reason)
        );
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestEmitSessionEvent()
    {
        var pairingTask = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;

            session = await _wallet.ApproveSession(id, new Namespaces
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

        await Task.WhenAll(
            pairingTask.Task,
            sessionApproval,
            _wallet.Pair(uriString)
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

        _dapp.SubscribeToSessionEvent(referenceTypeEventData.Name, ReferenceTypeEventHandler);
        _dapp.SubscribeToSessionEvent(valueTypeEventData.Name, ValueTypeEventHandler);

        await Task.WhenAll(
            referenceHandlingTask.Task,
            valueHandlingTask.Task,
            _wallet.EmitSessionEvent(session.Topic, referenceTypeEventData, TestRequiredNamespaces["eip155"].Chains[0]),
            _wallet.EmitSessionEvent(session.Topic, valueTypeEventData, TestRequiredNamespaces["eip155"].Chains[0])
        );

        Assert.True(_dapp.TryUnsubscribeFromSessionEvent(referenceTypeEventData.Name, ReferenceTypeEventHandler));
        Assert.True(_dapp.TryUnsubscribeFromSessionEvent(valueTypeEventData.Name, ValueTypeEventHandler));

        // Test invalid chains
        await Assert.ThrowsAsync<FormatException>(() => _wallet.EmitSessionEvent(session.Topic, valueTypeEventData, "invalid chain"));
        await Assert.ThrowsAsync<NamespacesException>(() => _wallet.EmitSessionEvent(session.Topic, valueTypeEventData, "123:321"));
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestGetActiveSessions()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _wallet.ApproveSession(id,
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

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
        );

        var sessions = _wallet.ActiveSessions;
        Assert.NotNull(sessions);
        Assert.Single(sessions);
        Assert.Equal(session.Topic, sessions.Values.ToArray()[0].Topic);
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestGetPendingSessionProposals()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += (sender, @event) =>
        {
            var proposals = _wallet.PendingSessionProposals;
            Assert.NotNull(proposals);
            Assert.Single(proposals);
            Assert.Equal(TestRequiredNamespaces, proposals.Values.ToArray()[0].RequiredNamespaces);
            task1.TrySetResult(true);
        };

        await Task.WhenAll(
            task1.Task,
            _wallet.Pair(uriString)
        );
    }

    [Fact(Timeout = 60_000)] [Trait("Category", "integration")]
    public async Task TestGetPendingSessionRequests()
    {
        var task1 = new TaskCompletionSource<bool>();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;
            var verifyContext = @event.VerifiedContext;

            session = await _wallet.ApproveSession(id,
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

        await Task.WhenAll(
            task1.Task,
            sessionApproval,
            _wallet.Pair(uriString)
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
        _wallet.Engine.SignClient.Engine.SessionRequestEvents<EthSignTransaction, string>()
            .OnRequest += args =>
        {
            // Get the pending session request, since that's what we're testing
            var pendingRequests = _wallet.PendingSessionRequests;
            var request = pendingRequests[0];

            var id = request.Id;
            var verifyContext = args.VerifiedContext;

            // Perform unsafe cast, all pending requests are stored as object type
            var signTransaction = ((EthSignTransaction)request.Parameters.Request.Params)[0];

            Assert.Equal(args.Request.Id, id);
            Assert.Equal(Validation.Unknown, verifyContext.Validation);

            var signature = ((AccountSignerTransactionManager)_cryptoWalletFixture.CryptoWallet.GetAccount(0).TransactionManager)
                .SignTransaction(signTransaction);

            args.Response = signature;
            task2.TrySetResult(true);
            return Task.CompletedTask;
        };

        async Task SendRequest()
        {
            var result = await _dapp.Request<EthSignTransaction, string>(session.Topic,
                requestParams, TestEthereumChain);

            Assert.False(string.IsNullOrWhiteSpace(result));
        }

        await Task.WhenAll(
            task2.Task,
            SendRequest()
        );
    }
}