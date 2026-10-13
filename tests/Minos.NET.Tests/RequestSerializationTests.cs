using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.SystemOne;
using Minos.SystemOne.Serialization;

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
            Model = SystemOneDefaults.Model,
            Questions = new Dictionary<string, Question>
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
            Model = SystemOneDefaults.Model,
            Questions = new Dictionary<string, Question>
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
            Model = SystemOneDefaults.Model,
            Questions = new Dictionary<string, Question>
            {
                ["department"] = new ChoiceQuestion
                {
                    Instructions = "Which team should handle this?",
                    Criteria = new Dictionary<string, DecisionContent?>
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
            Model = SystemOneDefaults.Model,
            Questions = new Dictionary<string, Question>
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
            State = DecisionContent.FromJson(Json("""
                {"resume":{"name":"John Smith","location":"Oakland, CA","last_employer":"Google"}}
                """)),
            Model = "jev-1.13.0",
            Questions = new Dictionary<string, Question>
            {
                ["is_duplicate"] = new NoulQuestion
                {
                    Instructions = DecisionContent.FromJson(Json("""
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
    public void Model_DefaultsToNull_SoTheClientCanFillItIn()
    {
        var request = new SystemOneRequest { State = State, Questions = new Dictionary<string, Question>() };

        Assert.Null(request.Model);
    }

    [Fact]
    public void SerializerOptions_ResolveTypesFromTheSourceGeneratedContext()
    {
        Assert.IsType<DecisionJsonContext>(Options.TypeInfoResolver);
    }

    [Fact]
    public void KeysWithCapitals_AreWrittenUnchanged()
    {
        var request = new SystemOneRequest
        {
            State = State,
            Model = SystemOneDefaults.Model,
            Questions = new Dictionary<string, Question>
            {
                ["IsUrgent"] = new ChoiceQuestion
                {
                    Instructions = "Which team should handle this?",
                    Criteria = new Dictionary<string, DecisionContent?> { ["Billing"] = "Payments and invoicing" },
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
            Model = SystemOneDefaults.Model,
            Questions = new Dictionary<string, Question>
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
        Assert.NotNull(Options.GetTypeInfo(typeof(SystemOneRequest)));
        Assert.NotNull(Options.GetTypeInfo(typeof(SystemOneResponse)));
        Assert.NotNull(Options.GetTypeInfo(typeof(ModelList)));
    }

    private static void AssertSerializesTo(SystemOneRequest request, string fixture)
    {
        var actual = JsonSerializer.SerializeToNode(request, DecisionJsonContext.Default.SystemOneRequest);
        var expected = Fixture.Load(fixture);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected {expected.ToJsonString()} but got {actual?.ToJsonString()}");
    }

    private static void AssertSerializesToJson(SystemOneRequest request, string expectedJson)
    {
        var actual = JsonSerializer.SerializeToNode(request, DecisionJsonContext.Default.SystemOneRequest);
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

    private static JsonSerializerOptions Options => DecisionJsonContext.Default.Options;
}
