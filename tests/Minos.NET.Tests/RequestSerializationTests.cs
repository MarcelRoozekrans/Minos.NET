using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Serialization;

namespace Minos.Tests;

public sealed class RequestSerializationTests
{
    private const string State = "Help! My payouts have been failing for 3 days.";

    [Fact]
    public void Noul_WithoutCriteria_OmitsCriteria()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        };

        AssertSerializesTo(request, "request-noul-minimal.json");
    }

    [Fact]
    public void Noul_WithCriteria_WritesTrueAndFalse()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["is_urgent"] = new NoulQuestion
                {
                    Instructions = "Does this convey urgency?",
                    Criteria = new NoulCriteria
                    {
                        WhenTrue = "Explicitly time-sensitive",
                        WhenFalse = "No urgency expressed",
                    },
                },
            },
        };

        AssertSerializesTo(request, "request-noul.json");
    }

    [Fact]
    public void Choice_WritesNullOptionDescriptions()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["department"] = new ChoiceQuestion
                {
                    Instructions = "Which team should handle this?",
                    Criteria = new Dictionary<string, JevContent?>
                    {
                        ["billing"] = "Payments, invoicing, refunds",
                        ["technical"] = "Bugs, outages, integrations",
                        ["sales"] = "Pricing, upgrades, new accounts",
                        ["other"] = null,
                    },
                },
            },
        };

        AssertSerializesTo(request, "request-choice.json");
    }

    [Fact]
    public void Score_WritesOrderedLevels()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["frustration"] = new ScoreQuestion
                {
                    Instructions = "How frustrated is the customer?",
                    Criteria = ["Calm", "Frustrated", "Very angry"],
                },
            },
        };

        AssertSerializesTo(request, "request-score.json");
    }

    [Fact]
    public void StructuredStateAndInstructions_AndPinnedModel_AreWrittenVerbatim()
    {
        var request = new SystemOneRequest
        {
            State = JevContent.FromJson(Json("""
                {"resume":{"name":"John Smith","location":"Oakland, CA","last_employer":"Google"}}
                """)),
            Model = "jev-1.13.0",
            Questions = new Dictionary<string, JevQuestion>
            {
                ["is_duplicate"] = new NoulQuestion
                {
                    Instructions = JevContent.FromJson(Json("""
                        {
                          "potential_duplicate": {"name":"John Smith","location":"Oakland, California","last_employer":"Google"},
                          "question": "Is the resume for the same person as `potential_duplicate`?"
                        }
                        """)),
                },
            },
        };

        AssertSerializesTo(request, "request-structured.json");
    }

    [Fact]
    public void Model_DefaultsToJevLatest()
    {
        var request = new SystemOneRequest { State = State, Questions = new Dictionary<string, JevQuestion>() };

        Assert.Equal(JevDefaults.Model, request.Model);
    }

    [Fact]
    public void JevJsonOptions_UseTheSourceGeneratedContext()
    {
        Assert.IsType<JevJsonContext>(JevJson.Options.TypeInfoResolver);
    }

    [Fact]
    public void NullModel_Throws()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Model = null!,
            Questions = new Dictionary<string, JevQuestion>(),
        };

        Assert.Throws<JsonException>(
            () => JsonSerializer.SerializeToNode(request, JevJsonContext.Default.SystemOneRequest));
    }

    [Fact]
    public void KeysWithCapitals_AreWrittenUnchanged()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["IsUrgent"] = new ChoiceQuestion
                {
                    Instructions = "Which team should handle this?",
                    Criteria = new Dictionary<string, JevContent?> { ["Billing"] = "Payments and invoicing" },
                },
            },
        };

        AssertSerializesToJson(request, """
            {
              "state": "Help! My payouts have been failing for 3 days.",
              "model": "jev-latest",
              "questions": {
                "IsUrgent": {
                  "type": "choice",
                  "instructions": "Which team should handle this?",
                  "criteria": { "Billing": "Payments and invoicing" }
                }
              }
            }
            """);
    }

    [Fact]
    public void NoulCriteria_WithOnlyWhenTrue_OmitsFalseKey()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Questions = new Dictionary<string, JevQuestion>
            {
                ["is_urgent"] = new NoulQuestion
                {
                    Instructions = "Does this convey urgency?",
                    Criteria = new NoulCriteria { WhenTrue = "Explicitly time-sensitive" },
                },
            },
        };

        AssertSerializesToJson(request, """
            {
              "state": "Help! My payouts have been failing for 3 days.",
              "model": "jev-latest",
              "questions": {
                "is_urgent": {
                  "type": "noul",
                  "instructions": "Does this convey urgency?",
                  "criteria": { "true": "Explicitly time-sensitive" }
                }
              }
            }
            """);
    }

    [Fact]
    public void TypeInfoResolver_ResolvesAllTopLevelTypes()
    {
        Assert.NotNull(JevJson.Options.GetTypeInfo(typeof(SystemOneRequest)));
        Assert.NotNull(JevJson.Options.GetTypeInfo(typeof(SystemOneResponse)));
        Assert.NotNull(JevJson.Options.GetTypeInfo(typeof(ModelList)));
    }

    private static void AssertSerializesTo(SystemOneRequest request, string fixture)
    {
        var actual = JsonSerializer.SerializeToNode(request, JevJsonContext.Default.SystemOneRequest);
        var expected = Fixture.Load(fixture);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected {expected.ToJsonString()} but got {actual?.ToJsonString()}");
    }

    private static void AssertSerializesToJson(SystemOneRequest request, string expectedJson)
    {
        var actual = JsonSerializer.SerializeToNode(request, JevJsonContext.Default.SystemOneRequest);
        var expected = JsonNode.Parse(expectedJson);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected {expected?.ToJsonString()} but got {actual?.ToJsonString()}");
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
