using Reown.Core;
using Reown.Core.Common.Logging;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Core.Storage;
using Reown.Core.Storage.Interfaces;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.TestUtils;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

public class SignTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _testOutputHelper;
    private SignClient _dapp;
    private SignClient _wallet;
    private InMemoryStorage _dappStorage;
    private InMemoryStorage _walletStorage;
    private const string AllowedChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    [RpcMethod("test_method")] [RpcRequestOptions(Clock.ONE_MINUTE, 99998)]
    public class TestRequest
    {
        public int a;
        public int b;
    }

    [RpcMethod("test_method_2")] [RpcRequestOptions(Clock.ONE_MINUTE, 99997)] [RpcResponseOptions(Clock.ONE_MINUTE, 99996)]
    public class TestRequest2
    {
        public string x;
        public int y;
    }

    // represents array of strings requests, similar to personal_sign
    [RpcMethod("complex_test_method")] [RpcRequestOptions(Clock.ONE_MINUTE, 99990)]
    public class ComplexTestRequest : List<string>
    {
        public ComplexTestRequest()
        {
        }

        public ComplexTestRequest(params string[] args) : base(args)
        {
        }

        public int A
        {
            get
            {
                if (Count != 2 || !int.TryParse(this[0], out var a))
                {
                    return 0;
                }

                return a;
            }
        }

        public int B
        {
            get
            {
                if (Count != 2 || !int.TryParse(this[1], out var b))
                {
                    return 0;
                }

                return b;
            }
        }
    }

    // represents array of objects requests, similar to eth_sendTransaction
    [RpcMethod("complex_test_method_2")] [RpcRequestOptions(Clock.ONE_MINUTE, 99991)] [RpcResponseOptions(Clock.ONE_MINUTE, 99992)]
    public class ComplexTestRequest2 : List<TestRequest2>
    {
        public ComplexTestRequest2()
        {
        }

        public ComplexTestRequest2(params TestRequest2[] args) : base(args)
        {
        }

        public string X
        {
            get => this.FirstOrDefault()?.x ?? string.Empty;
        }

        public int Y
        {
            get => this.FirstOrDefault()?.y ?? -1;
        }
    }

    [RpcResponseOptions(Clock.ONE_MINUTE, 99999)]
    public class TestResponse
    {
        public int result;
    }

    public SignTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public async Task InitializeAsync()
    {
        _dappStorage = new InMemoryStorage();
        _walletStorage = new InMemoryStorage();

        await InitializeDappClient(_dappStorage);
        await InitializeWallet(_walletStorage);

        ReownLogger.Instance = new TestOutputHelperLogger(_testOutputHelper);
    }

    private async Task InitializeDappClient(IKeyValueStorage storage)
    {
        _dapp = await SignClient.Init(new SignClientOptions
        {
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Metadata = new Metadata
            {
                Description = "Dapp Test",
                Icons =
                [
                    "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/reown-avatar-positive.png"
                ],
                Name = "Dapp",
                Url = "https://reown.com"
            },
            Storage = storage
        });
    }

    private async Task InitializeWallet(IKeyValueStorage storage)
    {
        _wallet = await SignClient.Init(new SignClientOptions
        {
            ProjectId = TestValues.TestProjectId,
            RelayUrl = TestValues.TestRelayUrl,
            Metadata = new Metadata
            {
                Description = "Wallet Test",
                Icons =
                [
                    "https://raw.githubusercontent.com/reown-com/reown-dotnet/main/media/reown-avatar-positive.png"
                ],
                Name = "Wallet",
                Url = "https://reown.com"
            },
            Storage = storage
        });
    }

    public async Task DisposeAsync()
    {
        ReownLogger.Instance = null;

        if (_dapp?.CoreClient != null)
        {
            await WaitForNoPendingRequests(_dapp);
            await _dapp.CoreClient.Storage.Clear();
            _dapp.Dispose();
        }

        if (_wallet?.CoreClient != null)
        {
            await WaitForNoPendingRequests(_wallet);
            await _wallet.CoreClient.Storage.Clear();
            _wallet.Dispose();
        }
    }

    public async Task WaitForNoPendingRequests(SignClient client)
    {
        if (client?.PendingSessionRequests == null)
            return;

        while (client.PendingSessionRequests.Length > 0)
        {
            ReownLogger.Log($"Waiting for {client.PendingSessionRequests.Length} requests to finish sending");
            await Task.Delay(100);
        }
    }

    private static async Task TestConnectMethod(ISignClient clientA, ISignClient clientB)
    {
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            "eth_sendTransaction",
                            "eth_signTransaction",
                            "eth_sign",
                            "personal_sign",
                            "eth_signTypedData"
                        },
                        Chains = new[]
                        {
                            "eip155:1",
                            "eip155:10"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var dappClient = clientA;
        var connectData = await dappClient.Connect(dappConnectOptions);

        var walletClient = clientB;

        var tcs = new TaskCompletionSource();
        walletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            var proposal = @event.Proposal;

            Assert.NotNull(proposal.RequiredNamespaces);
            Assert.NotNull(proposal.OptionalNamespaces);
            Assert.True(proposal.SessionProperties == null || proposal.SessionProperties.Count > 0);
            Assert.NotNull(proposal.Expiry);
            Assert.NotNull(proposal.Relays);
            Assert.NotNull(proposal.Proposer);
            Assert.NotNull(proposal.PairingTopic);

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{testAddress}")
                .WithAccount($"eip155:10:{testAddress}");

            var approveParams = new ApproveParams
            {
                Id = id,
                Namespaces = approvedNamespaces
            };

            var approveData = await walletClient.Approve(approveParams);
            await approveData.Acknowledged();

            await Task.Delay(500);

            tcs.SetResult();
        };

        _ = await walletClient.Pair(connectData.Uri);

        await tcs.Task;

        _ = await connectData.Approval;
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestApproveSession()
    {
        await TestConnectMethod(_dapp, _wallet);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestRejectSession()
    {
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            "eth_sendTransaction",
                            "eth_signTransaction",
                            "eth_sign",
                            "personal_sign",
                            "eth_signTypedData"
                        },
                        Chains = new[]
                        {
                            "eip155:1",
                            "eip155:10"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var connectData = await _dapp.Connect(dappConnectOptions);

        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{testAddress}")
                .WithAccount($"eip155:10:{testAddress}");

            await _wallet.Reject(new RejectParams
            {
                Id = id,
                Reason = Error.FromErrorType(ErrorType.GENERIC)
            });

            await Task.Delay(500);
        };

        _ = await _wallet.Pair(connectData.Uri);

        await Assert.ThrowsAsync<ReownNetworkException>(() => connectData.Approval);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestSessionRequestResponse()
    {
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "test_method";

        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            testMethod
                        },
                        Chains = new[]
                        {
                            "eip155:1",
                            "eip155:10"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var connectData = await _dapp.Connect(dappConnectOptions);

        var walletClient = _wallet;
        walletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{testAddress}")
                .WithAccount($"eip155:10:{testAddress}");

            var approveParams = new ApproveParams
            {
                Id = id,
                Namespaces = approvedNamespaces
            };

            var approveData = await walletClient.Approve(approveParams);
            await approveData.Acknowledged();
        };

        _ = await _wallet.Pair(connectData.Uri);
        var sessionData = await connectData.Approval;

        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);

        var testData = new TestRequest
        {
            a = a,
            b = b
        };

        var pending = new TaskCompletionSource<int>();

        // Step 1. Setup event listener for request

        // The wallet client will listen for the request with the "test_method" rpc method
        _wallet.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            requestData.Response = new TestResponse
            {
                result = data.a * data.b
            };

            return Task.CompletedTask;
        };

        // The dapp client will listen for the response
        // Normally, we wouldn't do this and just rely on the return value
        // from the dappClient.Engine.Request function call (the response Result or throws an Exception)
        // We do it here for the sake of testing
        _dapp.SessionRequestEvents<TestRequest, TestResponse>()
            .FilterResponses((r) => r.Topic == sessionData.Topic)
            .OnResponse += (responseData) =>
        {
            var response = responseData.Response;

            var data = response.Result;

            pending.SetResult(data.result);

            return Task.CompletedTask;
        };

        _dapp.SessionRequestSent += (sender, @event) => Assert.Equal(@event.Topic, sessionData.Topic);

        // 2. Send the request from the dapp client
        var responseReturned = await _dapp.Request<TestRequest, TestResponse>(sessionData.Topic, testData);

        // 3. Wait for the response from the event listener
        var eventResult = await pending.Task.WithTimeout(TimeSpan.FromSeconds(5));

        Assert.Equal(eventResult, a * b);
        Assert.Equal(eventResult, testData.a * testData.b);
        Assert.Equal(eventResult, responseReturned.result);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestSessionRequestInvalidMethod()
    {
        var validMethod = "test_method";

        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            validMethod
                        },
                        Chains = new[]
                        {
                            "eip155:1",
                            "eip155:10"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var connectData = await _dapp.Connect(dappConnectOptions);

        var walletClient = _wallet;
        walletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045")
                .WithAccount($"eip155:10:0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045");

            var approveParams = new ApproveParams
            {
                Id = id,
                Namespaces = approvedNamespaces
            };

            var approveData = await walletClient.Approve(approveParams);
            await approveData.Acknowledged();
        };
        _ = await _wallet.Pair(connectData.Uri);

        // var approveData = await walletClient.Approve(proposal, "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045");

        var sessionData = await connectData.Approval;
        // await approveData.Acknowledged();

        var testData = new TestRequest2
        {
            x = "test",
            y = 4
        };

        // Use TestRequest2 which isn't included in the required namespaces
        await Assert.ThrowsAsync<NamespacesException>(() => _dapp.Request<TestRequest2, TestResponse>(sessionData.Topic, testData));
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestInvalidConnect()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _dapp.Connect(null));

        var connectOptions = new ConnectOptions
        {
            PairingTopic = "123"
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _dapp.Connect(connectOptions));
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestTwoUniqueSessionRequestResponse()
    {
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "test_method";
        var testMethod2 = "test_method_2";

        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            testMethod,
                            testMethod2
                        },
                        Chains = new[]
                        {
                            "eip155:1",
                            "eip155:10"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var connectData = await _dapp.Connect(dappConnectOptions);

        var walletClient = _wallet;
        walletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{testAddress}")
                .WithAccount($"eip155:10:{testAddress}");

            var approveParams = new ApproveParams
            {
                Id = id,
                Namespaces = approvedNamespaces
            };

            var approveData = await walletClient.Approve(approveParams);
            await approveData.Acknowledged();
        };

        _ = await _wallet.Pair(connectData.Uri);

        var sessionData = await connectData.Approval;

        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);
        var x = rnd.NextStrings(AllowedChars, (Math.Min(a, b), Math.Max(a, b)), 1).First();
        var y = x.Length;

        var testData = new TestRequest
        {
            a = a,
            b = b
        };
        var testData2 = new TestRequest2
        {
            x = x,
            y = y
        };

        var pending = new TaskCompletionSource<int>();
        var pending2 = new TaskCompletionSource<bool>();

        // Step 1. Setup event listener for request

        // The wallet client will listen for the request with the "test_method" rpc method
        _wallet.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            requestData.Response = new TestResponse
            {
                result = data.a * data.b
            };

            return Task.CompletedTask;
        };

        // The wallet client will listen for the request with the "test_method" rpc method
        _wallet.Engine.SessionRequestEvents<TestRequest2, bool>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            requestData.Response = data.x.Length == data.y;

            return Task.CompletedTask;
        };

        // The dapp client will listen for the response
        // Normally, we wouldn't do this and just rely on the return value
        // from the dappClient.Engine.Request function call (the response Result or throws an Exception)
        // We do it here for the sake of testing
        _dapp.SessionRequestEvents<TestRequest, TestResponse>()
            .FilterResponses((r) => r.Topic == sessionData.Topic)
            .OnResponse += (responseData) =>
        {
            var response = responseData.Response;

            var data = response.Result;

            pending.TrySetResult(data.result);

            return Task.CompletedTask;
        };

        // 2. Send the request from the dapp client
        var responseReturned = await _dapp.Request<TestRequest, TestResponse>(sessionData.Topic, testData);
        var responseReturned2 = await _dapp.Request<TestRequest2, bool>(sessionData.Topic, testData2);

        // 3. Wait for the response from the event listener
        var eventResult = await pending.Task.WithTimeout(TimeSpan.FromSeconds(5));

        Assert.Equal(eventResult, a * b);
        Assert.Equal(eventResult, testData.a * testData.b);
        Assert.Equal(eventResult, responseReturned.result);

        Assert.True(responseReturned2);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestTwoUniqueComplexSessionRequestResponse()
    {
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "complex_test_method";
        var testMethod2 = "complex_test_method_2";

        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            testMethod,
                            testMethod2
                        },
                        Chains = new[]
                        {
                            "eip155:1",
                            "eip155:10"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var connectData = await _dapp.Connect(dappConnectOptions);

        var walletClient = _wallet;
        walletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{testAddress}")
                .WithAccount($"eip155:10:{testAddress}");

            var approveParams = new ApproveParams
            {
                Id = id,
                Namespaces = approvedNamespaces
            };

            var approveData = await walletClient.Approve(approveParams);
            await approveData.Acknowledged();
        };
        _ = await _wallet.Pair(connectData.Uri);

        var sessionData = await connectData.Approval;

        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);
        var x = rnd.NextStrings(AllowedChars, (Math.Min(a, b), Math.Max(a, b)), 1).First();
        var y = x.Length;

        var testData = new ComplexTestRequest(a.ToString(), b.ToString());
        var testData2 = new ComplexTestRequest2(new TestRequest2
        {
            x = x,
            y = y
        });

        var pending = new TaskCompletionSource<int>();

        // Step 1. Setup event listener for request

        // The wallet client will listen for the request with the "test_method" rpc method
        _wallet.Engine.SessionRequestEvents<ComplexTestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            requestData.Response = new TestResponse
            {
                result = data.A * data.B
            };

            return Task.CompletedTask;
        };

        // The wallet client will listen for the request with the "test_method" rpc method
        _wallet.Engine.SessionRequestEvents<ComplexTestRequest2, bool>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            requestData.Response = data.X.Length == data.Y;

            return Task.CompletedTask;
        };

        // The dapp client will listen for the response
        // Normally, we wouldn't do this and just rely on the return value
        // from the dappClient.Engine.Request function call (the response Result or throws an Exception)
        // We do it here for the sake of testing
        _dapp.SessionRequestEvents<ComplexTestRequest, TestResponse>()
            .FilterResponses((r) => r.Topic == sessionData.Topic)
            .OnResponse += (responseData) =>
        {
            var response = responseData.Response;

            var data = response.Result;

            pending.TrySetResult(data.result);

            return Task.CompletedTask;
        };

        // 2. Send the request from the dapp client
        var responseReturned = await _dapp.Request<ComplexTestRequest, TestResponse>(sessionData.Topic, testData);
        var responseReturned2 = await _dapp.Request<ComplexTestRequest2, bool>(sessionData.Topic, testData2);

        // 3. Wait for the response from the event listener
        var eventResult = await pending.Task.WithTimeout(TimeSpan.FromSeconds(5));

        Assert.Equal(eventResult, a * b);
        Assert.Equal(eventResult, testData.A * testData.B);
        Assert.Equal(eventResult, responseReturned.result);

        Assert.True(responseReturned2);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestTwoUniqueSessionRequestUsingAddressProviderDefaults()
    {
        await TwoUniqueSessionRequestUsingAddressProviderDefaults();
    }

    private async Task TwoUniqueSessionRequestUsingAddressProviderDefaults()
    {
        await _wallet.AddressProvider.LoadDefaultsAsync();
        await _dapp.AddressProvider.LoadDefaultsAsync();

        if (!_dapp.AddressProvider.HasDefaultSession && !_wallet.AddressProvider.HasDefaultSession)
        {
            var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
            var testMethod = "test_method";
            var testMethod2 = "test_method_2";

            var dappConnectOptions = new ConnectOptions
            {
                RequiredNamespaces = new RequiredNamespaces
                {
                    {
                        "eip155", new ProposedNamespace
                        {
                            Methods = new[]
                            {
                                testMethod,
                                testMethod2
                            },
                            Chains = new[]
                            {
                                "eip155:1"
                            },
                            Events = new[]
                            {
                                "chainChanged",
                                "accountsChanged"
                            }
                        }
                    }
                }
            };

            var tcs = new TaskCompletionSource();
            _wallet.SessionProposed += async (sender, @event) =>
            {
                var id = @event.Id;

                var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
                approvedNamespaces["eip155"].WithAccount($"eip155:1:{testAddress}");

                var approveParams = new ApproveParams
                {
                    Id = id,
                    Namespaces = approvedNamespaces
                };

                var approveData = await _wallet.Approve(approveParams);
                await approveData.Acknowledged();

                tcs.SetResult();
            };

            var connectData = await _dapp.Connect(dappConnectOptions);

            _ = await _wallet.Pair(connectData.Uri);

            await tcs.Task;

            _ = await connectData.Approval;
        }
        else
        {
            Assert.True(_dapp.AddressProvider.HasDefaultSession);
            Assert.True(_wallet.AddressProvider.HasDefaultSession);
        }

        var defaultSessionTopic = _dapp.AddressProvider.DefaultSession.Topic;
        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);
        var x = rnd.NextStrings(AllowedChars, (Math.Min(a, b), Math.Max(a, b)), 1).First();
        var y = x.Length;

        var testData = new TestRequest
        {
            a = a,
            b = b
        };
        var testData2 = new TestRequest2
        {
            x = x,
            y = y
        };

        var pending = new TaskCompletionSource<int>();
        var pending2 = new TaskCompletionSource<bool>();

        // Step 1. Setup event listener for request

        // The wallet client will listen for the request with the "test_method" rpc method
        _wallet.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            requestData.Response = new TestResponse
            {
                result = data.a * data.b
            };

            return Task.CompletedTask;
        };

        // The wallet client will listen for the request with the "test_method" rpc method
        _wallet.Engine.SessionRequestEvents<TestRequest2, bool>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            requestData.Response = data.x.Length == data.y;

            return Task.CompletedTask;
        };

        // The dapp client will listen for the response
        // Normally, we wouldn't do this and just rely on the return value
        // from the dappClient.Engine.Request function call (the response Result or throws an Exception)
        // We do it here for the sake of testing
        _dapp.SessionRequestEvents<TestRequest, TestResponse>()
            .FilterResponses((r) => r.Topic == defaultSessionTopic)
            .OnResponse += (responseData) =>
        {
            Assert.True(responseData.Topic == defaultSessionTopic);

            var response = responseData.Response;

            var data = response.Result;

            pending.TrySetResult(data.result);

            return Task.CompletedTask;
        };

        // 2. Send the request from the dapp client
        var responseReturned = await _dapp.Request<TestRequest, TestResponse>(testData);
        var responseReturned2 = await _dapp.Request<TestRequest2, bool>(testData2);

        // 3. Wait for the response from the event listener
        var eventResult = await pending.Task.WithTimeout(TimeSpan.FromSeconds(5));

        Assert.Equal(eventResult, a * b);
        Assert.Equal(eventResult, testData.a * testData.b);
        Assert.Equal(eventResult, responseReturned.result);

        Assert.True(responseReturned2);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestAddressProviderDefaults()
    {
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "test_method";
        var testMethod2 = "test_method_2";

        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            testMethod,
                            testMethod2
                        },
                        Chains = new[]
                        {
                            "eip155:1"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var connectData = await _dapp.Connect(dappConnectOptions);

        var tcs = new TaskCompletionSource();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"].WithAccount($"eip155:1:{testAddress}");

            var approveParams = new ApproveParams
            {
                Id = id,
                Namespaces = approvedNamespaces
            };

            var approveData = await _wallet.Approve(approveParams);
            await approveData.Acknowledged();

            tcs.SetResult();
        };

        _ = await _wallet.Pair(connectData.Uri);

        await tcs.Task;

        _ = await connectData.Approval;

        var address = _dapp.AddressProvider.CurrentAccount();
        Assert.Equal(testAddress, address.Address);
        Assert.Equal("eip155:1", address.ChainId);
        Assert.Equal("eip155:1", _dapp.AddressProvider.DefaultChainId);
        Assert.Equal("eip155", _dapp.AddressProvider.DefaultNamespace);

        address = _wallet.AddressProvider.CurrentAccount();
        Assert.Equal(testAddress, address.Address);
        Assert.Equal("eip155:1", address.ChainId);
        Assert.Equal("eip155:1", _dapp.AddressProvider.DefaultChainId);
        Assert.Equal("eip155", _dapp.AddressProvider.DefaultNamespace);

        var allAddresses = _dapp.AddressProvider.AllAccounts("eip155").ToArray();
        Assert.Single(allAddresses);
        Assert.Equal(testAddress, allAddresses[0].Address);
        Assert.Equal("eip155:1", allAddresses[0].ChainId);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestAddressProviderDefaultsSaving()
    {
        await TwoUniqueSessionRequestUsingAddressProviderDefaults();

        var defaultSessionTopic = _dapp.AddressProvider.DefaultSession.Topic;

        Assert.NotNull(defaultSessionTopic);
        await Task.Delay(500);

        _dapp.Dispose();
        _wallet.Dispose();

        await Task.Delay(500);

        await InitializeDappClient(_dappStorage);
        await InitializeWallet(_walletStorage);

        await Task.Delay(500);

        await _dapp.AddressProvider.LoadDefaultsAsync();
        var reloadedDefaultSessionTopic = _dapp.AddressProvider.DefaultSession.Topic;

        Assert.Equal(defaultSessionTopic, reloadedDefaultSessionTopic);

        await TwoUniqueSessionRequestUsingAddressProviderDefaults();
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestAddressProviderChainIdChange()
    {
        await TestConnectMethod(_dapp, _wallet);

        const string badChainId = "badChainId";
        await Assert.ThrowsAsync<ArgumentException>(() => _dapp.AddressProvider.SetDefaultChainIdAsync(badChainId));

        // Change the default chain id
        const string newChainId = "eip155:10";
        await _dapp.AddressProvider.SetDefaultChainIdAsync(newChainId);
        Assert.Equal(newChainId, _dapp.AddressProvider.DefaultChainId);
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestAddressProviderDisconnect()
    {
        await TestConnectMethod(_dapp, _wallet);

        Assert.True(_dapp.AddressProvider.HasDefaultSession);

        await _dapp.Disconnect();

        Assert.False(_dapp.AddressProvider.HasDefaultSession);
    }

    [Fact]
    [Trait("Category", "integration")]
    public async Task TestSessionRequestError()
    {
        const string testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        const string testMethod = "test_method";

        var dappConnectOptions = new ConnectOptions
        {
            RequiredNamespaces = new RequiredNamespaces
            {
                {
                    "eip155", new ProposedNamespace
                    {
                        Methods = new[]
                        {
                            testMethod
                        },
                        Chains = new[]
                        {
                            "eip155:1",
                            "eip155:10"
                        },
                        Events = new[]
                        {
                            "chainChanged",
                            "accountsChanged"
                        }
                    }
                }
            }
        };

        var connectData = await _dapp.Connect(dappConnectOptions);

        var tcs = new TaskCompletionSource();
        _wallet.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;

            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"]
                .WithAccount($"eip155:1:{testAddress}")
                .WithAccount($"eip155:10:{testAddress}");

            var approveParams = new ApproveParams
            {
                Id = id,
                Namespaces = approvedNamespaces
            };

            var approveData = await _wallet.Approve(approveParams);
            await approveData.Acknowledged();

            tcs.SetResult();
        };

        _ = await _wallet.Pair(connectData.Uri);

        await tcs.Task;

        var sessionData = await connectData.Approval;

        var testData = new TestRequest
        {
            a = -1, // Invalid input that should trigger an error
            b = -1
        };

        // The wallet client will respond with an error for invalid input
        _wallet.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;

            if (data.a < 0 || data.b < 0)
            {
                requestData.Error = Error.FromErrorType(ErrorType.GENERIC, "Negative numbers are not allowed");
                return Task.CompletedTask;
            }

            requestData.Response = new TestResponse
            {
                result = data.a * data.b
            };

            return Task.CompletedTask;
        };

        // Verify that the request throws an exception with the expected error
        var exception = await Assert.ThrowsAsync<ReownNetworkException>(() => _dapp.Request<TestRequest, TestResponse>(sessionData.Topic, testData)
        );

        Assert.Equal(ErrorType.GENERIC, exception.CodeType);
    }
}