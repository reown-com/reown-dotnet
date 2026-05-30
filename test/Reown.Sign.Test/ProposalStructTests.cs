using Reown.Core.Common.Model.Errors;
using Reown.Core.Models.Relay;
using Reown.Core.Network.Models;
using Reown.Sign.Models;
using Xunit;

namespace Reown.Sign.Test;

[Trait("Category", "unit")]
public class ProposalStructTests
{
    private static ProposalStruct CreateProposal()
    {
        var required = new RequiredNamespaces
        {
            {
                "eip155", new ProposedNamespace()
                    .WithChain("eip155:1")
                    .WithChain("eip155:10")
                    .WithMethod("eth_sign")
                    .WithEvent("chainChanged")
            }
        };

        return new ProposalStruct
        {
            Id = 123,
            Relays =
            [
                new ProtocolOptions
                {
                    Protocol = "irn"
                }
            ],
            RequiredNamespaces = required
        };
    }

    [Fact]
    public void Key_ReturnsId()
    {
        var proposal = CreateProposal();

        Assert.Equal(proposal.Id, proposal.Key);
    }

    [Fact]
    public void ApproveProposal_SingleAccount_BuildsCaip10AccountPerChain()
    {
        var proposal = CreateProposal();

        var approve = proposal.ApproveProposal("0xabc");

        Assert.Equal(123, approve.Id);
        Assert.Equal("irn", approve.RelayProtocol);

        var ns = approve.Namespaces["eip155"];
        Assert.Contains("eip155:1:0xabc", ns.Accounts);
        Assert.Contains("eip155:10:0xabc", ns.Accounts);
        Assert.Equal(new[]
        {
            "eth_sign"
        }, ns.Methods);
    }

    [Fact]
    public void ApproveProposal_MultipleAccounts_BuildsAccountPerChainAccountPair()
    {
        var proposal = CreateProposal();

        var approve = proposal.ApproveProposal(["0xabc", "0xdef"]);

        var accounts = approve.Namespaces["eip155"].Accounts;
        Assert.Equal(4, accounts.Length);
        Assert.Contains("eip155:1:0xabc", accounts);
        Assert.Contains("eip155:1:0xdef", accounts);
        Assert.Contains("eip155:10:0xabc", accounts);
        Assert.Contains("eip155:10:0xdef", accounts);
    }

    [Fact]
    public void ApproveProposal_IncludesOptionalNamespaces()
    {
        var proposal = CreateProposal();
        proposal.OptionalNamespaces = new Dictionary<string, ProposedNamespace>
        {
            ["solana"] = new ProposedNamespace().WithChain("solana:mainnet").WithMethod("solana_signMessage")
        };

        var approve = proposal.ApproveProposal(["0xabc"]);

        Assert.True(approve.Namespaces.ContainsKey("solana"));
        Assert.Contains("solana:mainnet:0xabc", approve.Namespaces["solana"].Accounts);
    }

    [Fact]
    public void ApproveProposal_UsesProvidedProtocolOption()
    {
        var proposal = CreateProposal();
        proposal.Relays =
        [
            new ProtocolOptions
            {
                Protocol = "irn"
            },
            new ProtocolOptions
            {
                Protocol = "waku"
            }
        ];

        var approve = proposal.ApproveProposal(["0xabc"], new ProtocolOptions
        {
            Protocol = "waku"
        });

        Assert.Equal("waku", approve.RelayProtocol);
    }

    [Fact]
    public void ApproveProposal_NoId_Throws()
    {
        var proposal = new ProposalStruct
        {
            Relays =
            [
                new ProtocolOptions
                {
                    Protocol = "irn"
                }
            ],
            RequiredNamespaces = new RequiredNamespaces()
        };

        Assert.Throws<InvalidOperationException>(() => proposal.ApproveProposal("0xabc"));
    }

    [Fact]
    public void ApproveProposal_UnknownProtocolOption_Throws()
    {
        var proposal = CreateProposal();

        Assert.Throws<InvalidOperationException>(() =>
            proposal.ApproveProposal(["0xabc"], new ProtocolOptions
            {
                Protocol = "unknown"
            }));
    }

    [Fact]
    public void RejectProposal_WithError_ReturnsRejectParams()
    {
        var proposal = CreateProposal();
        var error = new Error
        {
            Code = (long)ErrorType.USER_DISCONNECTED,
            Message = "denied"
        };

        var reject = proposal.RejectProposal(error);

        Assert.Equal(123, reject.Id);
        Assert.Equal(error, reject.Reason);
    }

    [Fact]
    public void RejectProposal_NullMessage_UsesDefaultReason()
    {
        var proposal = CreateProposal();

        var reject = proposal.RejectProposal();

        Assert.Equal((long)ErrorType.USER_DISCONNECTED, reject.Reason.Code);
        Assert.Equal("Proposal denied by remote host", reject.Reason.Message);
    }

    [Fact]
    public void RejectProposal_NoId_Throws()
    {
        var proposal = new ProposalStruct();

        Assert.Throws<InvalidOperationException>(() => proposal.RejectProposal("reason"));
    }
}