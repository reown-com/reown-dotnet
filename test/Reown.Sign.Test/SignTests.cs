using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Network.Models;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.TestUtils;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

public class SignTests : IClassFixture<SignClientFixture>
{
    private SignClientFixture _cryptoFixture;
    private readonly ITestOutputHelper _testOutputHelper;
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

    public SignClient ClientA
    {
        get => _cryptoFixture.ClientA;
    }

    public SignClient ClientB
    {
        get => _cryptoFixture.ClientB;
    }

    public SignTests(SignClientFixture cryptoFixture, ITestOutputHelper testOutputHelper)
    {
        _cryptoFixture = cryptoFixture;
        _testOutputHelper = testOutputHelper;
    }

    private static async Task TestConnectMethod(ISignClient clientA, ISignClient clientB)
    {
        var start = Clock.NowMilliseconds();
    
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var dappConnectOptions = new ConnectOptions()
        {
            RequiredNamespaces = new RequiredNamespaces()
            {
                {
                    "eip155", new ProposedNamespace()
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
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
    
        await TestConnectMethod(ClientA, ClientB);
    }
    
    [Fact] [Trait("Category", "integration")]
    public async Task TestRejectSession()
    {
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
    
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var dappConnectOptions = new ConnectOptions()
        {
            RequiredNamespaces = new RequiredNamespaces()
            {
                {
                    "eip155", new ProposedNamespace()
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
    
        var dappClient = ClientA;
        var connectData = await dappClient.Connect(dappConnectOptions);
    
        var walletClient = ClientB;
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

            await walletClient.Reject(new RejectParams
            {
                Id = id,
                Reason = Error.FromErrorType(ErrorType.GENERIC)
            });
            
            await Task.Delay(500);
        };
        
        _ = await walletClient.Pair(connectData.Uri);
    
        await Assert.ThrowsAsync<ReownNetworkException>(() => connectData.Approval);

        await _cryptoFixture.DisposeAndReset();
    }
    
    [Fact] [Trait("Category", "integration")]
    public async Task TestSessionRequestResponse()
    {
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
    
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "test_method";
    
        var dappConnectOptions = new ConnectOptions()
        {
            RequiredNamespaces = new RequiredNamespaces()
            {
                {
                    "eip155", new ProposedNamespace()
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
    
        var dappClient = ClientA;
        var connectData = await dappClient.Connect(dappConnectOptions);
    
        var walletClient = ClientB;
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

        _ = await walletClient.Pair(connectData.Uri);
        var sessionData = await connectData.Approval;
    
        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);
    
        var testData = new TestRequest()
        {
            a = a,
            b = b
        };
    
        var pending = new TaskCompletionSource<int>();
    
        // Step 1. Setup event listener for request
    
        // The wallet client will listen for the request with the "test_method" rpc method
        walletClient.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;
    
            requestData.Response = new TestResponse()
            {
                result = data.a * data.b
            };
    
            return Task.CompletedTask;
        };
    
        // The dapp client will listen for the response
        // Normally, we wouldn't do this and just rely on the return value
        // from the dappClient.Engine.Request function call (the response Result or throws an Exception)
        // We do it here for the sake of testing
        dappClient.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .FilterResponses((r) => r.Topic == sessionData.Topic)
            .OnResponse += (responseData) =>
        {
            var response = responseData.Response;
    
            var data = response.Result;
    
            pending.SetResult(data.result);
    
            return Task.CompletedTask;
        };
    
        // 2. Send the request from the dapp client
        var responseReturned = await dappClient.Engine.Request<TestRequest, TestResponse>(sessionData.Topic, testData);
    
        // 3. Wait for the response from the event listener
        var eventResult = await pending.Task.WithTimeout(TimeSpan.FromSeconds(5));
    
        Assert.Equal(eventResult, a * b);
        Assert.Equal(eventResult, testData.a * testData.b);
        Assert.Equal(eventResult, responseReturned.result);
    }
    
    [Fact] [Trait("Category", "integration")]
    public async Task TestSessionRequestInvalidMethod()
    {
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
    
        var validMethod = "test_method";
    
        var dappConnectOptions = new ConnectOptions()
        {
            RequiredNamespaces = new RequiredNamespaces()
            {
                {
                    "eip155", new ProposedNamespace()
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
    
        var dappClient = ClientA;
        var connectData = await dappClient.Connect(dappConnectOptions);
    
        var walletClient = ClientB;
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
        _ = await walletClient.Pair(connectData.Uri);
    
        // var approveData = await walletClient.Approve(proposal, "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045");
    
        var sessionData = await connectData.Approval;
        // await approveData.Acknowledged();
    
        var testData = new TestRequest2
        {
            x = "test",
            y = 4
        };
    
        // Use TestRequest2 which isn't included in the required namespaces
        await Assert.ThrowsAsync<NamespacesException>(() => dappClient.Engine.Request<TestRequest2, TestResponse>(sessionData.Topic, testData));
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestInvalidConnect()
    {
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
        var dappClient = ClientA;

        await Assert.ThrowsAsync<ArgumentNullException>(() => dappClient.Connect(null));

        var connectOptions = new ConnectOptions
        {
            PairingTopic = "123"
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => dappClient.Connect(connectOptions));
    }

    [Fact] [Trait("Category", "integration")]
    public async Task TestTwoUniqueSessionRequestResponse()
    {
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
    
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "test_method";
        var testMethod2 = "test_method_2";
    
        var dappConnectOptions = new ConnectOptions()
        {
            RequiredNamespaces = new RequiredNamespaces()
            {
                {
                    "eip155", new ProposedNamespace()
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
    
        var dappClient = ClientA;
        var connectData = await dappClient.Connect(dappConnectOptions);
    
        var walletClient = ClientB;
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
        
        _ = await walletClient.Pair(connectData.Uri);
        
        var sessionData = await connectData.Approval;
    
        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);
        var x = rnd.NextStrings(AllowedChars, (Math.Min(a, b), Math.Max(a, b)), 1).First();
        var y = x.Length;
    
        var testData = new TestRequest()
        {
            a = a,
            b = b
        };
        var testData2 = new TestRequest2()
        {
            x = x,
            y = y
        };
    
        var pending = new TaskCompletionSource<int>();
        var pending2 = new TaskCompletionSource<bool>();
    
        // Step 1. Setup event listener for request
    
        // The wallet client will listen for the request with the "test_method" rpc method
        walletClient.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;
    
            requestData.Response = new TestResponse()
            {
                result = data.a * data.b
            };
    
            return Task.CompletedTask;
        };
    
        // The wallet client will listen for the request with the "test_method" rpc method
        walletClient.Engine.SessionRequestEvents<TestRequest2, bool>()
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
        dappClient.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .FilterResponses((r) => r.Topic == sessionData.Topic)
            .OnResponse += (responseData) =>
        {
            var response = responseData.Response;
    
            var data = response.Result;
    
            pending.TrySetResult(data.result);
    
            return Task.CompletedTask;
        };
    
        // 2. Send the request from the dapp client
        var responseReturned = await dappClient.Engine.Request<TestRequest, TestResponse>(sessionData.Topic, testData);
        var responseReturned2 = await dappClient.Engine.Request<TestRequest2, bool>(sessionData.Topic, testData2);
    
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
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
    
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "complex_test_method";
        var testMethod2 = "complex_test_method_2";
    
        var dappConnectOptions = new ConnectOptions()
        {
            RequiredNamespaces = new RequiredNamespaces()
            {
                {
                    "eip155", new ProposedNamespace()
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
    
        var dappClient = ClientA;
        var connectData = await dappClient.Connect(dappConnectOptions);
    
        var walletClient = ClientB;
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
        _ = await walletClient.Pair(connectData.Uri);
        
        var sessionData = await connectData.Approval;
        
        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);
        var x = rnd.NextStrings(AllowedChars, (Math.Min(a, b), Math.Max(a, b)), 1).First();
        var y = x.Length;
    
        var testData = new ComplexTestRequest(a.ToString(), b.ToString());
        var testData2 = new ComplexTestRequest2(new TestRequest2()
        {
            x = x,
            y = y
        });
    
        var pending = new TaskCompletionSource<int>();
    
        // Step 1. Setup event listener for request
    
        // The wallet client will listen for the request with the "test_method" rpc method
        walletClient.Engine.SessionRequestEvents<ComplexTestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;
    
            requestData.Response = new TestResponse()
            {
                result = data.A * data.B
            };
    
            return Task.CompletedTask;
        };
    
        // The wallet client will listen for the request with the "test_method" rpc method
        walletClient.Engine.SessionRequestEvents<ComplexTestRequest2, bool>()
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
        dappClient.Engine.SessionRequestEvents<ComplexTestRequest, TestResponse>()
            .FilterResponses((r) => r.Topic == sessionData.Topic)
            .OnResponse += (responseData) =>
        {
            var response = responseData.Response;
    
            var data = response.Result;
    
            pending.TrySetResult(data.result);
    
            return Task.CompletedTask;
        };
    
        // 2. Send the request from the dapp client
        var responseReturned = await dappClient.Engine.Request<ComplexTestRequest, TestResponse>(sessionData.Topic, testData);
        var responseReturned2 = await dappClient.Engine.Request<ComplexTestRequest2, bool>(sessionData.Topic, testData2);
    
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
        await _cryptoFixture.WaitForClientsReady();
        await _cryptoFixture.DisposeAndReset();
        
        await TwoUniqueSessionRequestUsingAddressProviderDefaults();
    }
    
    private async Task TwoUniqueSessionRequestUsingAddressProviderDefaults()
    {
        await _cryptoFixture.WaitForClientsReady();
        
        var dappClient = ClientA;
        var walletClient = ClientB;
    
        await dappClient.AddressProvider.LoadDefaultsAsync();
        await walletClient.AddressProvider.LoadDefaultsAsync();
    
        if (!dappClient.AddressProvider.HasDefaultSession && !walletClient.AddressProvider.HasDefaultSession)
        {
            var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
            var testMethod = "test_method";
            var testMethod2 = "test_method_2";
    
            var dappConnectOptions = new ConnectOptions()
            {
                RequiredNamespaces = new RequiredNamespaces()
                {
                    {
                        "eip155", new ProposedNamespace()
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
            walletClient.SessionProposed += async (sender, @event) =>
            {
                var id = @event.Id;
                
                var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
                approvedNamespaces["eip155"].WithAccount($"eip155:1:{testAddress}");
    
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
    
            var connectData = await dappClient.Connect(dappConnectOptions);
    
            _ = await walletClient.Pair(connectData.Uri);
            
            await tcs.Task;
            
            _ = await connectData.Approval;
        }
        else
        {
            Assert.True(dappClient.AddressProvider.HasDefaultSession);
            Assert.True(walletClient.AddressProvider.HasDefaultSession);
        }

        var defaultSessionTopic = dappClient.AddressProvider.DefaultSession.Topic;
        var rnd = new Random();
        var a = rnd.Next(100);
        var b = rnd.Next(100);
        var x = rnd.NextStrings(AllowedChars, (Math.Min(a, b), Math.Max(a, b)), 1).First();
        var y = x.Length;
    
        var testData = new TestRequest()
        {
            a = a,
            b = b
        };
        var testData2 = new TestRequest2()
        {
            x = x,
            y = y
        };
    
        var pending = new TaskCompletionSource<int>();
        var pending2 = new TaskCompletionSource<bool>();
    
        // Step 1. Setup event listener for request
    
        // The wallet client will listen for the request with the "test_method" rpc method
        walletClient.Engine.SessionRequestEvents<TestRequest, TestResponse>()
            .OnRequest += (requestData) =>
        {
            var request = requestData.Request;
            var data = request.Params;
    
            requestData.Response = new TestResponse()
            {
                result = data.a * data.b
            };
    
            return Task.CompletedTask;
        };
    
        // The wallet client will listen for the request with the "test_method" rpc method
        walletClient.Engine.SessionRequestEvents<TestRequest2, bool>()
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
        dappClient.Engine.SessionRequestEvents<TestRequest, TestResponse>()
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
        var responseReturned = await dappClient.Engine.Request<TestRequest, TestResponse>(testData);
        var responseReturned2 = await dappClient.Engine.Request<TestRequest2, bool>(testData2);
    
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
        await _cryptoFixture.DisposeAndReset();
        await _cryptoFixture.WaitForClientsReady();
    
        var testAddress = "0xd8dA6BF26964aF9D7eEd9e03E53415D37aA96045";
        var testMethod = "test_method";
        var testMethod2 = "test_method_2";
    
        var dappConnectOptions = new ConnectOptions()
        {
            RequiredNamespaces = new RequiredNamespaces()
            {
                {
                    "eip155", new ProposedNamespace()
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
    
        var dappClient = ClientA;
        var connectData = await dappClient.Connect(dappConnectOptions);
    
        var walletClient = ClientB;
        var tcs = new TaskCompletionSource();
        walletClient.SessionProposed += async (sender, @event) =>
        {
            var id = @event.Id;
            
            var approvedNamespaces = new Namespaces(@event.Proposal.RequiredNamespaces);
            approvedNamespaces["eip155"].WithAccount($"eip155:1:{testAddress}");
    
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
        
        var address = dappClient.AddressProvider.CurrentAddress();
        Assert.Equal(testAddress, address.Address);
        Assert.Equal("eip155:1", address.ChainId);
        Assert.Equal("eip155:1", dappClient.AddressProvider.DefaultChainId);
        Assert.Equal("eip155", dappClient.AddressProvider.DefaultNamespace);
    
        address = walletClient.AddressProvider.CurrentAddress();
        Assert.Equal(testAddress, address.Address);
        Assert.Equal("eip155:1", address.ChainId);
        Assert.Equal("eip155:1", dappClient.AddressProvider.DefaultChainId);
        Assert.Equal("eip155", dappClient.AddressProvider.DefaultNamespace);
    
        var allAddresses = dappClient.AddressProvider.AllAddresses("eip155").ToArray();
        Assert.Single(allAddresses);
        Assert.Equal(testAddress, allAddresses[0].Address);
        Assert.Equal("eip155:1", allAddresses[0].ChainId);
    }
    
    [Fact] [Trait("Category", "integration")]
    public async Task TestAddressProviderDefaultsSaving()
    {
        await _cryptoFixture.WaitForClientsReady();
    
        await TwoUniqueSessionRequestUsingAddressProviderDefaults();
    
        var defaultSessionTopic = _cryptoFixture.ClientA.AddressProvider.DefaultSession.Topic;
    
        Assert.NotNull(defaultSessionTopic);
    
        _cryptoFixture.StorageOverrideA = _cryptoFixture.ClientA.CoreClient.Storage;
        _cryptoFixture.StorageOverrideB = _cryptoFixture.ClientB.CoreClient.Storage;
    
        await Task.Delay(500);
    
        _cryptoFixture.ClientA.Dispose();
        _cryptoFixture.ClientB.Dispose();
        await _cryptoFixture.Init();
    
        await Task.Delay(500);
    
        await _cryptoFixture.ClientA.AddressProvider.LoadDefaultsAsync();
        var reloadedDefaultSessionTopic = _cryptoFixture.ClientA.AddressProvider.DefaultSession.Topic;
    
        Assert.Equal(defaultSessionTopic, reloadedDefaultSessionTopic);
    
        await TwoUniqueSessionRequestUsingAddressProviderDefaults();
    }
    
    [Fact] [Trait("Category", "integration")]
    public async Task TestAddressProviderChainIdChange()
    {
        await _cryptoFixture.WaitForClientsReady();
        await _cryptoFixture.DisposeAndReset();
    
        await TestConnectMethod(ClientA, ClientB);
        
        const string badChainId = "badChainId";
        await Assert.ThrowsAsync<ArgumentException>(() => ClientA.AddressProvider.SetDefaultChainIdAsync(badChainId));
    
        // Change the default chain id
        const string newChainId = "eip155:10";
        await ClientA.AddressProvider.SetDefaultChainIdAsync(newChainId);
        Assert.Equal(newChainId, ClientA.AddressProvider.DefaultChainId);
    }
    
    [Fact] [Trait("Category", "integration")]
    public async Task TestAddressProviderDisconnect()
    {
        await _cryptoFixture.WaitForClientsReady();
        await _cryptoFixture.DisposeAndReset();
    
        await TestConnectMethod(ClientA, ClientB);
        
        Assert.True(ClientA.AddressProvider.HasDefaultSession);
    
        await ClientA.Disconnect();
    
        Assert.False(ClientA.AddressProvider.HasDefaultSession);
    }
}