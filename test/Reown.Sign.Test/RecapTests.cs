using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reown.Sign.Utils;
using Xunit;
using Xunit.Abstractions;

namespace Reown.Sign.Test;

public class RecapTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public RecapTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    private static (string resource, string ability, string[] chains, Dictionary<string, object> limits) GetRecapProperties()
    {
        const string resource = "eip155";
        const string ability = "request";
        var chains = new[]
        {
            "eip155:1",
            "eip155:2",
            "eip155:3"
        };
        var actions = new[]
        {
            "eth_signTypedData_v4",
            "personal_sign"
        };
        var limits = new Dictionary<string, object>
        {
            { "chains", chains }
        };

        return (resource, ability, actions, limits);
    }

    [Fact] [Trait("Category", "unit")]
    public void IsReCap_WithValidResource_ReturnsTrue()
    {
        const string resource =
            "urn:recap:eyJhdHQiOnsiaHR0cHM6Ly9leGFtcGxlLmNvbS9waWN0dXJlcy8iOnsiY3J1ZC9kZWxldGUiOlt7fV0sImNydWQvdXBkYXRlIjpbe31dLCJvdGhlci9hY3Rpb24iOlt7fV19LCJtYWlsdG86dXNlcm5hbWVAZXhhbXBsZS5jb20iOnsibXNnL3JlY2VpdmUiOlt7Im1heF9jb3VudCI6NSwidGVtcGxhdGVzIjpbIm5ld3NsZXR0ZXIiLCJtYXJrZXRpbmciXX1dLCJtc2cvc2VuZCI6W3sidG8iOiJzb21lb25lQGVtYWlsLmNvbSJ9LHsidG8iOiJqb2VAZW1haWwuY29tIn1dfX0sInByZiI6WyJ6ZGo3V2o2Rk5TNHJVVWJzaUp2amp4Y3NOcVpkRENTaVlSOHNLUVhmb1BmcFNadUF3Il19\n";
        Assert.True(ReCapUtils.IsReCap(resource));
    }

    [Theory]
    [InlineData("InvalidResource")]
    [InlineData(":urn:recap")]
    [InlineData(null)]
    [InlineData("")]
    public void IsReCap_WithInvalidResource_ReturnsFalse(string resource)
    {
        Assert.False(ReCapUtils.IsReCap(resource));
    }

    [Fact] [Trait("Category", "unit")]
    public void CreateEncodedRecap_EmptyLimits_ReturnsExpectedResult()
    {
        var (resource, ability, actions, _) = GetRecapProperties();

        var encodedRecap = ReCapUtils.CreateEncodedRecap(resource, ability, actions, null);

        const string expectedEncodedRecap = "urn:recap:eyJhdHQiOnsiZWlwMTU1Ijp7InJlcXVlc3QvZXRoX3NpZ25UeXBlZERhdGFfdjQiOlt7fV0sInJlcXVlc3QvcGVyc29uYWxfc2lnbiI6W3t9XX19fQ";

        Assert.Equal(expectedEncodedRecap, encodedRecap);
    }

    [Fact] [Trait("Category", "unit")]
    public void AssignAbilityToActions_WithValidArgs_ReturnsExpectedResult()
    {
        var (_, ability, actions, limits) = GetRecapProperties();

        Dictionary<string, Dictionary<string, object>[]> result = ReCapUtils.AssignAbilityToActions(ability, actions, limits);

        var expected = new Dictionary<string, Dictionary<string, object>[]>
        {
            {
                "request/eth_signTypedData_v4", [
                    new Dictionary<string, object>
                    {
                        {
                            "chains", new[]
                            {
                                "eip155:1",
                                "eip155:2",
                                "eip155:3"
                            }
                        }
                    }
                ]
            },
            {
                "request/personal_sign", [
                    new Dictionary<string, object>
                    {
                        {
                            "chains", new[]
                            {
                                "eip155:1",
                                "eip155:2",
                                "eip155:3"
                            }
                        }
                    }
                ]
            }
        };

        Assert.Equal(expected, result);
    }

    [Fact] [Trait("Category", "unit")]
    public void CreateRecap_WithValidArgs_ReturnsExpectedResult()
    {
        var (resource, ability, actions, limits) = GetRecapProperties();

        var result = ReCapUtils.CreateRecap(resource, ability, actions, limits);

        Assert.NotNull(result);

        const string expectedRecapStr =
            """
            {"att":{"eip155":{"request/eth_signTypedData_v4":[{"chains":["eip155:1","eip155:2","eip155:3"]}],"request/personal_sign":[{"chains":["eip155:1","eip155:2","eip155:3"]}]}}}
            """;

        var resultJson = JsonConvert.SerializeObject(result);

        Assert.Equal(expectedRecapStr, resultJson);
    }

    [Fact] [Trait("Category", "unit")]
    public void CreateEncodedRecap_WithValidArgs_ReturnsExpectedResult()
    {
        var (resource, ability, actions, limits) = GetRecapProperties();
        var chains = limits["chains"] as string[];

        var encodedRecap = ReCapUtils.CreateEncodedRecap(resource, ability, actions, limits);

        const string expectedEncodedRecap =
            "urn:recap:eyJhdHQiOnsiZWlwMTU1Ijp7InJlcXVlc3QvZXRoX3NpZ25UeXBlZERhdGFfdjQiOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dLCJyZXF1ZXN0L3BlcnNvbmFsX3NpZ24iOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dfX19";

        Assert.Equal(expectedEncodedRecap, encodedRecap);

        var decodedRecap = ReCapUtils.DecodeRecap(encodedRecap);

        Assert.NotNull(decodedRecap);

        Assert.NotNull(decodedRecap.Att);

        Assert.True(decodedRecap.Att.ContainsKey("eip155"), "Decoded recap should contain 'eip155' key under 'att'.");

        var eip155Section = decodedRecap.Att["eip155"];
        Assert.NotNull(eip155Section);

        Assert.Equal(2, eip155Section.Properties.Keys.Count);
        Assert.Contains("request/eth_signTypedData_v4", eip155Section.Properties.Keys);
        Assert.Contains("request/personal_sign", eip155Section.Properties.Keys);

        foreach (var action in actions)
        {
            var actionKey = $"{ability}/{action}";
            Assert.True(eip155Section.Properties.ContainsKey(actionKey), $"Action '{actionKey}' should exist.");

            var actionEntries = eip155Section.Properties[actionKey] as JArray;
            Assert.NotNull(actionEntries);
            Assert.Single(actionEntries);

            var actionEntry = actionEntries.First as JObject;
            Assert.NotNull(actionEntry);

            var chainsToken = actionEntry["chains"];
            Assert.NotNull(chainsToken);

            var chainsArray = chainsToken.ToObject<string[]>();
            Assert.NotNull(chainsArray);
            Assert.Equal(chains.Length, chainsArray.Length);
            Assert.Equal(chains, chainsArray);
        }

        var expectedJson = new JObject
        {
            ["att"] = new JObject
            {
                ["eip155"] = new JObject
                {
                    ["request/eth_signTypedData_v4"] = new JArray
                    {
                        new JObject
                        {
                            ["chains"] = new JArray(chains)
                        }
                    },
                    ["request/personal_sign"] = new JArray
                    {
                        new JObject
                        {
                            ["chains"] = new JArray(chains)
                        }
                    }
                }
            }
        };

        var decodedRecapJson = JObject.FromObject(decodedRecap);
        Assert.True(JToken.DeepEquals(decodedRecapJson, expectedJson), "Decoded recap JSON does not match the expected JSON structure.");
    }

    [Fact] [Trait("Category", "unit")]
    public void ValidateRecap_WithValidRecap_DoesNotThrow()
    {
        const string validRecap =
            "urn:recap:eyJhdHQiOnsiZWlwMTU1Ijp7InJlcXVlc3QvZXRoX3NpZ25UeXBlZERhdGFfdjQiOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dLCJyZXF1ZXN0L3BlcnNvbmFsX3NpZ24iOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dfX19";

        var recap = ReCapUtils.DecodeRecap(validRecap);
        ReCapUtils.ValidateRecap(recap); // Should not throw
    }

    [Fact] [Trait("Category", "unit")]
    public void ValidateRecap_NullRecap_ThrowsArgumentException()
    {
        ReCap recap = null;

        var exception = Assert.Throws<ArgumentException>(() => ReCapUtils.ValidateRecap(recap));

        Assert.Equal("No `att` property found", exception.Message);
    }

    [Fact] [Trait("Category", "unit")]
    public void ValidateRecap_NullAtt_ThrowsArgumentException()
    {
        var recap = new ReCap
        {
            Att = null
        };

        var exception = Assert.Throws<ArgumentException>(() => ReCapUtils.ValidateRecap(recap));

        Assert.Equal("No `att` property found", exception.Message);
    }

    [Fact] [Trait("Category", "unit")]
    public void ValidateRecap_NullProperties_ThrowsArgumentException()
    {
        var recap = new ReCap
        {
            Att = new Dictionary<string, AttValue>
            {
                {
                    "resource1", new AttValue
                    {
                        Properties = null
                    }
                }
            }
        };

        var exception = Assert.Throws<ArgumentException>(() => ReCapUtils.ValidateRecap(recap));

        Assert.Contains("Resource object is empty or null", exception.Message);
    }

    [Fact] [Trait("Category", "unit")]
    public void ValidateRecap_EmptyProperties_ThrowsArgumentException()
    {
        var recap = new ReCap
        {
            Att = new Dictionary<string, AttValue>
            {
                {
                    "resource1", new AttValue
                    {
                        Properties = new Dictionary<string, JToken>()
                    }
                }
            }
        };

        var exception = Assert.Throws<ArgumentException>(() => ReCapUtils.ValidateRecap(recap));

        Assert.Contains("Resource object is empty or null", exception.Message);
    }

    [Fact] [Trait("Category", "unit")]
    public void ValidateRecap_AbilityNotJArray_ThrowsArgumentException()
    {
        var recap = new ReCap
        {
            Att = new Dictionary<string, AttValue>
            {
                {
                    "resource1", new AttValue
                    {
                        Properties = new Dictionary<string, JToken>
                        {
                            { "ability1", JToken.FromObject("NotAnArray") }
                        }
                    }
                }
            }
        };

        var exception = Assert.Throws<ArgumentException>(() => ReCapUtils.ValidateRecap(recap));

        Assert.Contains("Ability 'ability1' must be an array.", exception.Message);
    }

    [Fact] [Trait("Category", "unit")]
    public void ValidateRecap_EmptyLimitsArray_ThrowsArgumentException()
    {
        var recap = new ReCap
        {
            Att = new Dictionary<string, AttValue>
            {
                {
                    "resource1", new AttValue
                    {
                        Properties = new Dictionary<string, JToken>
                        {
                            { "ability1", JArray.FromObject(new List<JToken>()) }
                        }
                    }
                }
            }
        };

        var exception = Assert.Throws<ArgumentException>(() => ReCapUtils.ValidateRecap(recap));

        Assert.Contains("Value of ability 'ability1' is an empty array; it must contain at least one limit object.", exception.Message);
    }

    [Fact] [Trait("Category", "unit")]
    public void GetMethodsFromRecap_WithValidRecap_ReturnsExpectedMethods()
    {
        const string recapStr =
            "urn:recap:eyJhdHQiOnsiZWlwMTU1Ijp7InJlcXVlc3QvZXRoX3NpZ25UeXBlZERhdGFfdjQiOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dLCJyZXF1ZXN0L3BlcnNvbmFsX3NpZ24iOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dfX19";

        var methods = ReCapUtils.GetMethodsFromRecap(recapStr);

        var expectedMethods = new[]
        {
            "eth_signTypedData_v4",
            "personal_sign"
        };

        Assert.Equal(expectedMethods, methods);
    }

    [Fact] [Trait("Category", "unit")]
    public void GetChainsFromRecap_WithValidRecap_ReturnsExpectedChains()
    {
        const string recapStr =
            "urn:recap:eyJhdHQiOnsiZWlwMTU1Ijp7InJlcXVlc3QvZXRoX3NpZ25UeXBlZERhdGFfdjQiOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dLCJyZXF1ZXN0L3BlcnNvbmFsX3NpZ24iOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dfX19";

        var chains = ReCapUtils.GetChainsFromRecap(recapStr);

        var expectedChains = new[]
        {
            "eip155:1",
            "eip155:2",
            "eip155:3"
        };

        Assert.Equal(expectedChains, chains);
    }

    [Fact] [Trait("Category", "unit")]
    public void FormatStatementFromRecap_WithValidRecap_ReturnsExpectedStatement()
    {
        const string recapStr =
            "urn:recap:eyJhdHQiOnsiZWlwMTU1Ijp7InJlcXVlc3QvZXRoX3NpZ25UeXBlZERhdGFfdjQiOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dLCJyZXF1ZXN0L3BlcnNvbmFsX3NpZ24iOlt7ImNoYWlucyI6WyJlaXAxNTU6MSIsImVpcDE1NToyIiwiZWlwMTU1OjMiXX1dfX19";

        var recap = ReCapUtils.DecodeRecap(recapStr);

        var formattedStatement = ReCapUtils.FormatStatementFromRecap(recap);

        const string expectedStatement = "I further authorize the stated URI to perform the following actions on my behalf: (1) 'request': 'eth_signTypedData_v4', 'personal_sign' for 'eip155'.";

        _testOutputHelper.WriteLine(formattedStatement);

        Assert.Equal(expectedStatement, formattedStatement);
    }

    [Fact] [Trait("Category", "unit")]
    public void MergeRecaps_WithOverlappingRecaps_ReturnsMergedRecap()
    {
        const string resource = "eip155";
        const string ability = "request";
        var actions1 = new[]
        {
            "eth_signTypedData_v4",
            "personal_sign"
        };

        var chains1 = new[]
        {
            "eip155:1",
            "eip155:2",
            "eip155:3"
        };
        var limits1 = new Dictionary<string, object>
        {
            { "chains", chains1 }
        };

        var recap1 = ReCapUtils.CreateRecap(resource, ability, actions1, limits1);

        var actions2 = new[]
        {
            "eth_signTypedData_v4",
            "personal_sign",
            "eth_sign"
        };

        var chains2 = new[]
        {
            "eip155:1",
            "eip155:4",
            "eip155:5",
            "eip155:6"
        };
        var limits2 = new Dictionary<string, object>
        {
            { "chains", chains2 }
        };

        var recap2 = ReCapUtils.CreateRecap(resource, ability, actions2, limits2);

        var mergedRecap = ReCapUtils.MergeRecaps(recap1, recap2);


        var recapJson = JsonConvert.SerializeObject(mergedRecap);
        _testOutputHelper.WriteLine(recapJson);


        Assert.NotNull(mergedRecap);

        Assert.True(mergedRecap.Att.ContainsKey(resource));

        var att1 = mergedRecap.Att[resource];

        Assert.NotNull(att1);

        Assert.True(att1.Properties.ContainsKey($"{ability}/eth_signTypedData_v4"));
        Assert.True(att1.Properties.ContainsKey($"{ability}/personal_sign"));
        Assert.True(att1.Properties.ContainsKey($"{ability}/eth_sign"));

        var extractedChains1 = att1.Properties[$"{ability}/eth_signTypedData_v4"][0]["chains"].ToObject<string[]>();
        Assert.NotNull(extractedChains1);
        Assert.Equal(6, extractedChains1.Length);
        Assert.Equal(extractedChains1, chains1.Union(chains2).ToArray());

        var extractedChains2 = att1.Properties[$"{ability}/eth_sign"][0]["chains"].ToObject<string[]>();
        Assert.NotNull(extractedChains2);
        Assert.Equal(4, extractedChains2.Length);
        Assert.Equal(extractedChains2, chains2);
    }

    [Fact] [Trait("Category", "unit")]
    public void MergeRecaps_WithEmptyLimits_ReturnsMergedRecap()
    {
        const string resource = "eip155";
        const string ability = "request";
        var actions1 = new[]
        {
            "eth_signTypedData_v4",
            "personal_sign"
        };

        var chains1 = new[]
        {
            "eip155:1",
            "eip155:2",
            "eip155:3"
        };
        var limits1 = new Dictionary<string, object>
        {
            { "chains", chains1 }
        };

        var recap1 = ReCapUtils.CreateRecap(resource, ability, actions1, limits1);

        var actions2 = new[]
        {
            "eth_signTypedData_v4",
            "personal_sign",
            "eth_sign"
        };

        Dictionary<string, object> limits2 = null;

        var recap2 = ReCapUtils.CreateRecap(resource, ability, actions2, limits2);

        var mergedRecap = ReCapUtils.MergeRecaps(recap1, recap2);

        var recapJson = JsonConvert.SerializeObject(mergedRecap);
        _testOutputHelper.WriteLine(recapJson);

        Assert.NotNull(mergedRecap);

        Assert.True(mergedRecap.Att.ContainsKey(resource));
        Assert.True(mergedRecap.Att[resource].Properties.ContainsKey($"{ability}/eth_signTypedData_v4"));
        Assert.True(mergedRecap.Att[resource].Properties.ContainsKey($"{ability}/personal_sign"));
        Assert.True(mergedRecap.Att[resource].Properties.ContainsKey($"{ability}/eth_sign"));

        var extractedChains1 = mergedRecap.Att[resource].Properties[$"{ability}/eth_signTypedData_v4"][0]["chains"].ToObject<string[]>();
        Assert.NotNull(extractedChains1);
        Assert.Equal(3, extractedChains1.Length);
        Assert.Equal(extractedChains1, chains1);

        Assert.Single(mergedRecap.Att[resource].Properties[$"{ability}/eth_sign"]);
        var jtoken = mergedRecap.Att[resource].Properties[$"{ability}/eth_sign"][0];
        Assert.IsType<JObject>(jtoken);
        Assert.Empty((JObject)jtoken);
    }

    [Fact] [Trait("Category", "unit")]
    public void MergeEncodedRecaps_WithValidRecaps_ReturnsMergedRecap()
    {
        const string encodedRecap1 = "urn:recap:eyJhdHQiOnsiaHR0cHM6Ly9leGFtcGxlMS5jb20iOnsiY3J1ZC9yZWFkIjpbe31dfX19";
        const string encodedRecap2 = "urn:recap:eyJhdHQiOnsiaHR0cHM6Ly9leGFtcGxlMS5jb20iOnsiY3J1ZC91cGRhdGUiOlt7Im1heF90aW1lcyI6MX1dfSwiaHR0cHM6Ly9leGFtcGxlMi5jb20iOnsiY3J1ZC9kZWxldGUiOlt7fV19fX0==";

        var mergedRecap = ReCapUtils.MergeEncodedRecaps(encodedRecap1, encodedRecap2);

        const string expectedMergedRecap = "urn:recap:eyJhdHQiOnsiaHR0cHM6Ly9leGFtcGxlMS5jb20iOnsiY3J1ZC9yZWFkIjpbe31dLCJjcnVkL3VwZGF0ZSI6W3sibWF4X3RpbWVzIjoxfV19LCJodHRwczovL2V4YW1wbGUyLmNvbSI6eyJjcnVkL2RlbGV0ZSI6W3t9XX19fQ";
        Assert.Equal(expectedMergedRecap, mergedRecap);

        _ = ReCapUtils.DecodeRecap(mergedRecap);
    }
}