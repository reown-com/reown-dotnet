using Reown.Core.Common.Utils;
using Reown.Core.Models.Relay;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine;
using Xunit;

namespace Reown.Sign.Test;

[Trait("Category", "unit")]
public class ConnectOptionsTests
{
    [Fact]
    public void DefaultConstructor_InitializesEmptyNamespaces()
    {
        var options = new ConnectOptions();

        Assert.NotNull(options.RequiredNamespaces);
        Assert.Empty(options.RequiredNamespaces);
        Assert.NotNull(options.OptionalNamespaces);
        Assert.Empty(options.OptionalNamespaces);
        Assert.Null(options.SessionProperties);
        Assert.Equal(Clock.FIVE_MINUTES, options.Expiry);
    }

    [Fact]
    public void ParameterizedConstructor_NullArgs_AppliesDefaults()
    {
        var options = new ConnectOptions(null, null, null, null, null);

        Assert.NotNull(options.RequiredNamespaces);
        Assert.NotNull(options.OptionalNamespaces);
        Assert.Null(options.SessionProperties);
        Assert.Equal("", options.PairingTopic);
        Assert.Null(options.Relays);
    }

    [Fact]
    public void ParameterizedConstructor_WithValues_AssignsProvided()
    {
        var required = new RequiredNamespaces();
        var optional = new Dictionary<string, ProposedNamespace>();
        var props = new Dictionary<string, string> { ["k"] = "v" };
        var relays = new ProtocolOptions { Protocol = "irn" };

        var options = new ConnectOptions(required, "topic", relays, optional, props);

        Assert.Same(required, options.RequiredNamespaces);
        Assert.Same(optional, options.OptionalNamespaces);
        Assert.Same(props, options.SessionProperties);
        Assert.Equal("topic", options.PairingTopic);
        Assert.Same(relays, options.Relays);
    }

    [Fact]
    public void RequireNamespace_AddsAndReturnsSameInstance()
    {
        var options = new ConnectOptions();

        var result = options.RequireNamespace("eip155", new ProposedNamespace().WithChain("eip155:1"));

        Assert.Same(options, result);
        Assert.True(options.RequiredNamespaces.ContainsKey("eip155"));
    }

    [Fact]
    public void WithOptionalNamespace_Adds()
    {
        var options = new ConnectOptions();

        options.WithOptionalNamespace("solana", new ProposedNamespace().WithChain("solana:1"));

        Assert.True(options.OptionalNamespaces.ContainsKey("solana"));
    }

    [Fact]
    public void AddSessionProperty_InitializesDictionaryWhenNull()
    {
        var options = new ConnectOptions();

        options.AddSessionProperty("k", "v");

        Assert.Equal("v", options.SessionProperties["k"]);
    }

    [Fact]
    public void WithSessionProperties_ReplacesDictionary()
    {
        var props = new Dictionary<string, string> { ["a"] = "b" };

        var options = new ConnectOptions().WithSessionProperties(props);

        Assert.Same(props, options.SessionProperties);
    }

    [Fact]
    public void UseRequireNamespaces_Replaces()
    {
        var required = new RequiredNamespaces();

        var options = new ConnectOptions().UseRequireNamespaces(required);

        Assert.Same(required, options.RequiredNamespaces);
    }

    [Fact]
    public void WithPairingTopic_Sets()
    {
        var options = new ConnectOptions().WithPairingTopic("pair-topic");

        Assert.Equal("pair-topic", options.PairingTopic);
    }

    [Fact]
    public void WithOptions_SetsRelays()
    {
        var relays = new ProtocolOptions { Protocol = "irn" };

        var options = new ConnectOptions().WithOptions(relays);

        Assert.Same(relays, options.Relays);
    }

    [Fact]
    public void WithExpiry_Seconds_SetsExpiry()
    {
        var options = new ConnectOptions().WithExpiry(120);

        Assert.Equal(120, options.Expiry);
    }

    [Fact]
    public void WithExpiry_TimeSpan_SetsExpiryInSeconds()
    {
        var options = new ConnectOptions().WithExpiry(TimeSpan.FromMinutes(2));

        Assert.Equal(120, options.Expiry);
    }

    [Fact]
    public void BuilderMethods_AreChainable()
    {
        var options = new ConnectOptions()
            .WithPairingTopic("t")
            .WithExpiry(60)
            .AddSessionProperty("k", "v")
            .RequireNamespace("eip155", new ProposedNamespace().WithChain("eip155:1"));

        Assert.Equal("t", options.PairingTopic);
        Assert.Equal(60, options.Expiry);
        Assert.True(options.RequiredNamespaces.ContainsKey("eip155"));
        Assert.Equal("v", options.SessionProperties["k"]);
    }
}
