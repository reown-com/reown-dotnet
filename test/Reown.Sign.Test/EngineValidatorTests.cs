using System;
using System.Collections.Generic;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Common.Utils;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Reown.Sign.Models.Engine.Methods;
using Xunit;

namespace Reown.Sign.Test
{
    /// <summary>
    ///     Unit tests for <see cref="EngineValidator" />. These tests exercise pure validation
    ///     logic without standing up a live <see cref="Engine" /> or relay client.
    /// </summary>
    public class EngineValidatorTests
    {
        private const string EthAccount = "eip155:1:0x0000000000000000000000000000000000000000";
        private const string EthChain = "eip155:1";

        private static Namespaces SingleEip155Namespace(
            string[]? accounts = null,
            string[]? methods = null,
            string[]? events = null)
        {
            return new Namespaces
            {
                ["eip155"] = new Namespace
                {
                    Accounts = accounts ?? new[] { EthAccount },
                    Methods = methods ?? new[] { "eth_sign" },
                    Events = events ?? new[] { "chainChanged" }
                }
            };
        }

        private static RequiredNamespaces SingleEip155Required(
            string[]? chains = null,
            string[]? methods = null,
            string[]? events = null)
        {
            return new RequiredNamespaces
            {
                ["eip155"] = new ProposedNamespace
                {
                    Chains = chains ?? new[] { EthChain },
                    Methods = methods ?? new[] { "eth_sign" },
                    Events = events ?? new[] { "chainChanged" }
                }
            };
        }

        // -------------------- GetAccountsChains --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void GetAccountsChains_ExtractsChainPrefix()
        {
            var chains = EngineValidator.GetAccountsChains(new[]
            {
                "eip155:1:0xabc",
                "eip155:137:0xdef",
                "solana:101:somepubkey"
            });

            Assert.Equal(new[] { "eip155:1", "eip155:137", "solana:101" }, chains);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void GetAccountsChains_EmptyInput_ReturnsEmpty()
        {
            var chains = EngineValidator.GetAccountsChains(Array.Empty<string>());
            Assert.Empty(chains);
        }

        // -------------------- HasOverlap --------------------

        [Theory]
        [Trait("Category", "unit")]
        [InlineData(new[] { "a", "b" }, new[] { "a", "b", "c" }, true)]
        [InlineData(new[] { "a" }, new[] { "a" }, true)]
        [InlineData(new string[0], new[] { "a" }, true)]
        [InlineData(new[] { "a", "b" }, new[] { "a" }, false)]
        [InlineData(new[] { "a" }, new string[0], false)]
        public void HasOverlap_ReturnsExpected(string[] a, string[] b, bool expected)
        {
            Assert.Equal(expected, EngineValidator.HasOverlap(a, b));
        }

        // -------------------- ValidateAccounts --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateAccounts_ValidAccount_DoesNotThrow()
        {
            EngineValidator.ValidateAccounts(new[] { EthAccount }, "ctx");
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData("nochain")]
        [InlineData("eip155:1")] // missing address
        [InlineData("eip155::0xabc")] // empty reference
        public void ValidateAccounts_InvalidAccount_ThrowsFormatOrArgument(string badAccount)
        {
            // IsValidAccountId throws ArgumentException on malformed ids; ValidateAccounts wraps
            // the boolean result in a FormatException. Either is acceptable from a contract view,
            // so we accept both.
            var ex = Record.Exception(() => EngineValidator.ValidateAccounts(new[] { badAccount }, "ctx"));
            Assert.NotNull(ex);
            Assert.True(ex is FormatException || ex is ArgumentException,
                $"Expected FormatException or ArgumentException but got {ex!.GetType().Name}: {ex.Message}");
        }

        // -------------------- ValidateNamespaces --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateNamespaces_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EngineValidator.ValidateNamespaces(null!, "approve()"));
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateNamespaces_ValidNamespaces_DoesNotThrow()
        {
            EngineValidator.ValidateNamespaces(SingleEip155Namespace(), "approve()");
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateNamespaces_BadlyFormattedAccount_Throws()
        {
            var ns = SingleEip155Namespace(accounts: new[] { "not-a-valid-account" });
            Assert.ThrowsAny<Exception>(() => EngineValidator.ValidateNamespaces(ns, "approve()"));
        }

        // -------------------- ValidateNamespacesChainId --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateNamespacesChainId_ValidChainPresent_DoesNotThrow()
        {
            EngineValidator.ValidateNamespacesChainId(SingleEip155Namespace(), EthChain);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateNamespacesChainId_InvalidChainIdFormat_ThrowsFormatException()
        {
            var ex = Assert.Throws<FormatException>(
                () => EngineValidator.ValidateNamespacesChainId(SingleEip155Namespace(), "not_a_chain_id"));
            Assert.Contains("CAIP-2", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateNamespacesChainId_ChainIdNotInNamespaces_ThrowsNamespacesException()
        {
            var ex = Assert.Throws<NamespacesException>(
                () => EngineValidator.ValidateNamespacesChainId(SingleEip155Namespace(), "eip155:999"));
            Assert.Contains("eip155:999", ex.Message);
        }

        // -------------------- ValidateConformingNamespaces --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateConformingNamespaces_NullRequired_DoesNotThrow()
        {
            EngineValidator.ValidateConformingNamespaces(null!, SingleEip155Namespace(), "update()");
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateConformingNamespaces_AllConform_DoesNotThrow()
        {
            EngineValidator.ValidateConformingNamespaces(
                SingleEip155Required(),
                SingleEip155Namespace(),
                "update()");
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateConformingNamespaces_MissingKey_Throws()
        {
            var required = new RequiredNamespaces
            {
                ["solana"] = new ProposedNamespace
                {
                    Chains = new[] { "solana:101" },
                    Methods = new[] { "sol_sign" },
                    Events = Array.Empty<string>()
                }
            };

            var ex = Assert.Throws<NamespacesException>(() =>
                EngineValidator.ValidateConformingNamespaces(required, SingleEip155Namespace(), "update()"));
            Assert.Contains("requiredNamespaces", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateConformingNamespaces_MissingChain_Throws()
        {
            var required = SingleEip155Required(chains: new[] { "eip155:1", "eip155:137" });
            var ex = Assert.Throws<NamespacesException>(() =>
                EngineValidator.ValidateConformingNamespaces(required, SingleEip155Namespace(), "update()"));
            Assert.Contains("chains", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateConformingNamespaces_MissingMethod_Throws()
        {
            var required = SingleEip155Required(methods: new[] { "eth_sign", "eth_signTypedData" });
            var ex = Assert.Throws<NamespacesException>(() =>
                EngineValidator.ValidateConformingNamespaces(required, SingleEip155Namespace(), "update()"));
            Assert.Contains("methods", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateConformingNamespaces_MissingEvent_Throws()
        {
            var required = SingleEip155Required(events: new[] { "chainChanged", "accountsChanged" });
            var ex = Assert.Throws<NamespacesException>(() =>
                EngineValidator.ValidateConformingNamespaces(required, SingleEip155Namespace(), "update()"));
            Assert.Contains("events", ex.Message);
        }

        // -------------------- GetNamespaces{Methods,Events,Chains}ForChainId --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void GetNamespacesMethodsForChainId_ReturnsMatchingMethods()
        {
            var ns = SingleEip155Namespace(methods: new[] { "eth_sign", "personal_sign" });
            var methods = EngineValidator.GetNamespacesMethodsForChainId(ns, EthChain);
            Assert.Equal(new[] { "eth_sign", "personal_sign" }, methods);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void GetNamespacesMethodsForChainId_UnknownChain_ReturnsEmpty()
        {
            var methods = EngineValidator.GetNamespacesMethodsForChainId(SingleEip155Namespace(), "eip155:999");
            Assert.Empty(methods);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void GetNamespacesEventsForChainId_ReturnsMatchingEvents()
        {
            var ns = SingleEip155Namespace(events: new[] { "chainChanged", "accountsChanged" });
            var events = EngineValidator.GetNamespacesEventsForChainId(ns, EthChain);
            Assert.Equal(new[] { "chainChanged", "accountsChanged" }, events);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void GetNamespacesChains_AggregatesFromAllNamespaces()
        {
            var ns = new Namespaces
            {
                ["eip155"] = new Namespace
                {
                    Accounts = new[] { EthAccount },
                    Methods = Array.Empty<string>(),
                    Events = Array.Empty<string>()
                },
                ["solana"] = new Namespace
                {
                    Accounts = new[] { "solana:101:pubkey" },
                    Methods = Array.Empty<string>(),
                    Events = Array.Empty<string>()
                }
            };

            var chains = EngineValidator.GetNamespacesChains(ns);
            Assert.Contains("eip155:1", chains);
            Assert.Contains("solana:101", chains);
        }

        // -------------------- IsSessionCompatible --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void IsSessionCompatible_FullMatch_ReturnsTrue()
        {
            var session = new Session { Namespaces = SingleEip155Namespace() };
            Assert.True(EngineValidator.IsSessionCompatible(session, SingleEip155Required()));
        }

        [Fact]
        [Trait("Category", "unit")]
        public void IsSessionCompatible_MissingRequiredKey_ReturnsFalse()
        {
            var session = new Session { Namespaces = SingleEip155Namespace() };
            var required = new RequiredNamespaces
            {
                ["solana"] = new ProposedNamespace
                {
                    Chains = new[] { "solana:101" },
                    Methods = Array.Empty<string>(),
                    Events = Array.Empty<string>()
                }
            };

            Assert.False(EngineValidator.IsSessionCompatible(session, required));
        }

        [Fact]
        [Trait("Category", "unit")]
        public void IsSessionCompatible_MissingMethod_ReturnsFalse()
        {
            var session = new Session { Namespaces = SingleEip155Namespace(methods: new[] { "eth_sign" }) };
            var required = SingleEip155Required(methods: new[] { "eth_sign", "eth_sendTransaction" });
            Assert.False(EngineValidator.IsSessionCompatible(session, required));
        }

        // -------------------- ValidateAuthParams --------------------

        private static AuthParams MakeValidAuthParams()
        {
            return new AuthParams(
                chains: new[] { EthChain },
                domain: "example.com",
                nonce: "abc123",
                uri: "https://example.com",
                nbf: null,
                exp: null,
                statement: null,
                requestId: null,
                resources: null,
                methods: null);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateAuthParams_Valid_DoesNotThrow()
        {
            EngineValidator.ValidateAuthParams(MakeValidAuthParams());
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateAuthParams_NullChains_ThrowsArgumentException()
        {
            var p = MakeValidAuthParams();
            p.Chains = null!;
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateAuthParams(p));
            Assert.Contains("Chains", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateAuthParams_EmptyChains_ThrowsArgumentException()
        {
            var p = MakeValidAuthParams();
            p.Chains = Array.Empty<string>();
            Assert.Throws<ArgumentException>(() => EngineValidator.ValidateAuthParams(p));
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void ValidateAuthParams_BlankUri_ThrowsArgumentException(string? uri)
        {
            var p = MakeValidAuthParams();
            p.Uri = uri!;
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateAuthParams(p));
            Assert.Contains("Uri", ex.Message);
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("\t")]
        public void ValidateAuthParams_BlankDomain_ThrowsArgumentException(string? domain)
        {
            var p = MakeValidAuthParams();
            p.Domain = domain!;
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateAuthParams(p));
            Assert.Contains("Domain", ex.Message);
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void ValidateAuthParams_BlankNonce_ThrowsArgumentException(string? nonce)
        {
            var p = MakeValidAuthParams();
            p.Nonce = nonce!;
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateAuthParams(p));
            Assert.Contains("Nonce", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateAuthParams_MultipleNamespaces_ThrowsArgumentException()
        {
            var p = MakeValidAuthParams();
            p.Chains = new[] { "eip155:1", "solana:101" };
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateAuthParams(p));
            Assert.Contains("Multi-namespace", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateAuthParams_NonEip155Namespace_ThrowsArgumentException()
        {
            var p = MakeValidAuthParams();
            p.Chains = new[] { "solana:101" };
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateAuthParams(p));
            Assert.Contains("eip155", ex.Message);
        }

        // -------------------- ValidateSessionSettleRequest --------------------

        private static SessionSettle MakeValidSettle()
        {
            return new SessionSettle
            {
                Controller = new Participant { PublicKey = "deadbeef" },
                Relay = new ProtocolOptions { Protocol = "irn" },
                Namespaces = SingleEip155Namespace(),
                Expiry = Clock.CalculateExpiry(Clock.ONE_HOUR)
            };
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateSessionSettleRequest_Valid_DoesNotThrow()
        {
            EngineValidator.ValidateSessionSettleRequest(MakeValidSettle());
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateSessionSettleRequest_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EngineValidator.ValidateSessionSettleRequest(null!));
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateSessionSettleRequest_BlankRelayProtocol_ThrowsArgumentException(string protocol)
        {
            var s = MakeValidSettle();
            s.Relay = new ProtocolOptions { Protocol = protocol };
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateSessionSettleRequest(s));
            Assert.Contains("Relay protocol", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateSessionSettleRequest_NullController_ThrowsArgumentException()
        {
            var s = MakeValidSettle();
            s.Controller = null!;
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateSessionSettleRequest(s));
            Assert.Contains("Controller", ex.Message);
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void ValidateSessionSettleRequest_BlankControllerKey_ThrowsArgumentException(string? key)
        {
            var s = MakeValidSettle();
            s.Controller = new Participant { PublicKey = key! };
            Assert.Throws<ArgumentException>(() => EngineValidator.ValidateSessionSettleRequest(s));
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateSessionSettleRequest_NullNamespaces_ThrowsArgumentNullException()
        {
            var s = MakeValidSettle();
            s.Namespaces = null!;
            Assert.Throws<ArgumentNullException>(() => EngineValidator.ValidateSessionSettleRequest(s));
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateSessionSettleRequest_ExpiredExpiry_ThrowsInvalidOperationException()
        {
            var s = MakeValidSettle();
            s.Expiry = 1; // far in the past
            var ex = Assert.Throws<InvalidOperationException>(() => EngineValidator.ValidateSessionSettleRequest(s));
            Assert.Contains("expired", ex.Message);
        }

        // -------------------- ValidateApproveOptions --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateApproveOptions_NullRelayProtocolAndNullSessionProperties_DoesNotThrow()
        {
            EngineValidator.ValidateApproveOptions(null, null);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateApproveOptions_NonEmptyRelayProtocol_DoesNotThrow()
        {
            EngineValidator.ValidateApproveOptions("irn", null);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateApproveOptions_ValidSessionProperties_DoesNotThrow()
        {
            var properties = new Dictionary<string, string>
            {
                ["foo"] = "bar",
                ["baz"] = "qux"
            };
            EngineValidator.ValidateApproveOptions("irn", properties);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateApproveOptions_EmptySessionPropertiesDictionary_DoesNotThrow()
        {
            EngineValidator.ValidateApproveOptions(null, new Dictionary<string, string>());
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        public void ValidateApproveOptions_BlankRelayProtocol_ThrowsArgumentException(string protocol)
        {
            var ex = Assert.Throws<ArgumentException>(
                () => EngineValidator.ValidateApproveOptions(protocol, null));
            Assert.Contains("RelayProtocol", ex.Message);
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ValidateApproveOptions_SessionPropertiesContainsBlankValue_ThrowsArgumentException(string? badValue)
        {
            var properties = new Dictionary<string, string>
            {
                ["good"] = "value",
                ["bad"] = badValue!
            };

            var ex = Assert.Throws<ArgumentException>(
                () => EngineValidator.ValidateApproveOptions(null, properties));
            Assert.Contains("SessionProperties", ex.Message);
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateApproveOptions_BlankRelayProtocolCheckedBeforeSessionProperties()
        {
            // Both are invalid; the relay-protocol check must fire first to preserve the
            // original ordering inside IsValidApprove.
            var properties = new Dictionary<string, string>
            {
                ["bad"] = null!
            };

            var ex = Assert.Throws<ArgumentException>(
                () => EngineValidator.ValidateApproveOptions("", properties));
            Assert.Contains("RelayProtocol", ex.Message);
        }

        // -------------------- ValidateRejectReason --------------------

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateRejectReason_ValidReason_DoesNotThrow()
        {
            EngineValidator.ValidateRejectReason(new Error { Code = 5000, Message = "user rejected" });
        }

        [Fact]
        [Trait("Category", "unit")]
        public void ValidateRejectReason_NullReason_ThrowsArgumentException()
        {
            var ex = Assert.Throws<ArgumentException>(() => EngineValidator.ValidateRejectReason(null!));
            Assert.Contains("Reject reason", ex.Message);
        }

        [Theory]
        [Trait("Category", "unit")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t")]
        public void ValidateRejectReason_BlankMessage_ThrowsArgumentException(string? message)
        {
            var ex = Assert.Throws<ArgumentException>(
                () => EngineValidator.ValidateRejectReason(new Error { Code = 5000, Message = message! }));
            Assert.Contains("Reject reason", ex.Message);
        }
    }
}
