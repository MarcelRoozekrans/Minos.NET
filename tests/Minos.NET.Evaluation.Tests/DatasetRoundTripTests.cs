using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Minos.Evaluation.Tests;

/// <summary>
/// Everything the System One writer sends for text criteria reads back to an equal definition. All eight request
/// fixtures use text criteria, so every one is read: none uses <c>Criterion.Json</c>, which version 1 leaves out.
/// </summary>
public sealed class DatasetRoundTripTests
{
    [Theory]
    [InlineData("request-built-set.json")]
    [InlineData("request-choice.json")]
    [InlineData("request-keyed.json")]
    [InlineData("request-noul-minimal.json")]
    [InlineData("request-noul.json")]
    [InlineData("request-score.json")]
    [InlineData("request-structured-criteria.json")]
    [InlineData("request-structured.json")]
    public void EveryFixture_ReadsBackToTheSameQuestions(string fixture)
    {
        var request = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture)))!.AsObject();
        var questions = request["questions"]!.AsObject();
        var line = new JsonObject
        {
            ["id"] = fixture,
            ["state"] = request["state"]!.DeepClone(),
            ["questions"] = questions.DeepClone(),
            ["expected"] = new JsonObject(),
        }.ToJsonString();

        var item = Assert.Single(ParseOk(line));

        Assert.Equal(Content(request["state"]!), item.State);
        var read = item.Definition.Questions;
        Assert.Equal(questions.Select(q => q.Key), read.Select(q => q.Key));
        for (var i = 0; i < read.Count; i++)
        {
            AssertMatchesJson(questions[i]!.AsObject(), read[i]);
        }
    }

    [Fact]
    public void ABuiltDefinition_WrittenAsSystemOneJson_ReadsBackEqual()
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("urgent", "Is it urgent?", out _, c => c
                .WhenTrue("Time-sensitive")
                .WhenFalse(DecisionContent.FromUtf8Json("""{"hint":"It can wait a day."}"""u8)))
            .Noul("spam", DecisionContent.FromUtf8Json("""{"question":"Is it spam?","examples":["Win a prize"]}"""u8), out _)
            .Choice("team", "Which team?", out _, o => o
                .Option("billing", Criterion.Text("Payments").WithExamples("I want a refund", "I was charged twice").WithNotFor("How much is Pro?"))
                .Option("technical", Criterion.Text("Bugs").WithNotFor("Feature requests"))
                .Option("sales", "Pricing")
                .Option("other"))
            .Score("mood", "How upset is the customer?", out _, l => l
                .Level(Criterion.Text("Calm").WithNotFor("Swearing"))
                .Level(Criterion.Text("Annoyed").WithExamples("This is the second time"))
                .Level("Angry"))
            .Build();
        Assert.True(built.IsSuccess);
        var definition = built.Value.Definition;
        var line = new JsonObject
        {
            ["id"] = "built",
            ["state"] = "s",
            ["questions"] = JsonNode.Parse(ToSystemOneQuestions(definition)),
            ["expected"] = new JsonObject { ["urgent"] = true, ["team"] = "billing", ["mood"] = 2 },
        }.ToJsonString();

        var item = Assert.Single(ParseOk(line));

        Assert.Equal(definition.Questions.Count, item.Definition.Questions.Count);
        for (var i = 0; i < definition.Questions.Count; i++)
        {
            AssertSameQuestion(definition.Questions[i], item.Definition.Questions[i]);
        }
    }

    private static DecisionDataset ParseOk(string jsonl)
    {
        var result = DecisionDataset.Parse(jsonl);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join("; ", result.Error.Select(p => $"{p.Line}: {p.Message}")));
        return result.Value;
    }

    private static void AssertMatchesJson(JsonObject json, QuestionDefinition question)
    {
        var type = json["type"]!.GetValue<string>();
        Assert.Equal(
            type switch
            {
                "noul" => QuestionKind.Noul,
                "choice" => QuestionKind.Choice,
                _ => QuestionKind.Score,
            },
            question.Kind);
        Assert.Equal(Content(json["instructions"]!), question.Instructions);
        var criteria = json["criteria"];
        switch (type)
        {
            case "noul":
                Assert.Equal(criteria?["true"] is { } whenTrue ? Content(whenTrue) : (DecisionContent?)null, question.WhenTrue);
                Assert.Equal(criteria?["false"] is { } whenFalse ? Content(whenFalse) : (DecisionContent?)null, question.WhenFalse);
                break;
            case "choice":
                var options = criteria!.AsObject().ToList();
                Assert.Equal(options.Select(o => o.Key), question.Options.Select(o => o.Key));
                for (var i = 0; i < options.Count; i++)
                {
                    AssertCriterionMatchesJson(options[i].Value, question.Options[i].Criterion);
                }

                break;
            default:
                var levels = criteria!.AsArray();
                Assert.Equal(
                    Enumerable.Range(0, levels.Count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    question.Options.Select(o => o.Key));
                for (var i = 0; i < levels.Count; i++)
                {
                    AssertCriterionMatchesJson(levels[i], question.Options[i].Criterion);
                }

                break;
        }
    }

    private static void AssertCriterionMatchesJson(JsonNode? json, Criterion? criterion)
    {
        if (json is null)
        {
            Assert.Null(criterion);
            return;
        }

        Assert.NotNull(criterion);
        Assert.Null(criterion.JsonContent);
        if (json is JsonValue text)
        {
            Assert.Equal(text.GetValue<string>(), criterion.Description);
            Assert.Empty(criterion.Examples);
            Assert.Empty(criterion.NotFor);
            return;
        }

        Assert.Equal(json["description"]!.GetValue<string>(), criterion.Description);
        Assert.Equal(Strings(json["examples"]), criterion.Examples);
        Assert.Equal(Strings(json["not_for"]), criterion.NotFor);
    }

    private static string[] Strings(JsonNode? array)
        => array is null ? [] : [.. array.AsArray().Select(s => s!.GetValue<string>())];

    private static DecisionContent Content(JsonNode node)
        => node is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? DecisionContent.FromString(value.GetValue<string>())
            : DecisionContent.FromUtf8Json(Encoding.UTF8.GetBytes(node.ToJsonString()));

    private static void AssertSameQuestion(QuestionDefinition expected, QuestionDefinition actual)
    {
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Instructions, actual.Instructions);
        Assert.Equal(expected.WhenTrue, actual.WhenTrue);
        Assert.Equal(expected.WhenFalse, actual.WhenFalse);
        Assert.Equal(expected.Options.Select(o => o.Key), actual.Options.Select(o => o.Key));
        for (var i = 0; i < expected.Options.Count; i++)
        {
            var left = expected.Options[i].Criterion;
            var right = actual.Options[i].Criterion;
            if (left is null)
            {
                Assert.Null(right);
                continue;
            }

            Assert.NotNull(right);
            Assert.Equal(left.Description, right.Description);
            Assert.Equal(left.Examples, right.Examples);
            Assert.Equal(left.NotFor, right.NotFor);
            Assert.Null(right.JsonContent);
        }
    }

    // The System One questions shape, written from the definition's public getters.
    private static string ToSystemOneQuestions(QuestionSetDefinition definition)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var question in definition.Questions)
            {
                writer.WriteStartObject(question.Key);
                writer.WriteString("type", question.Kind switch
                {
                    QuestionKind.Noul => "noul",
                    QuestionKind.Choice => "choice",
                    _ => "score",
                });
                writer.WritePropertyName("instructions");
                WriteContent(writer, question.Instructions);
                switch (question.Kind)
                {
                    case QuestionKind.Noul:
                        if (question.WhenTrue is not null || question.WhenFalse is not null)
                        {
                            writer.WriteStartObject("criteria");
                            if (question.WhenTrue is { } whenTrue)
                            {
                                writer.WritePropertyName("true");
                                WriteContent(writer, whenTrue);
                            }

                            if (question.WhenFalse is { } whenFalse)
                            {
                                writer.WritePropertyName("false");
                                WriteContent(writer, whenFalse);
                            }

                            writer.WriteEndObject();
                        }

                        break;
                    case QuestionKind.Choice:
                        writer.WriteStartObject("criteria");
                        foreach (var option in question.Options)
                        {
                            writer.WritePropertyName(option.Key);
                            WriteCriterion(writer, option.Criterion);
                        }

                        writer.WriteEndObject();
                        break;
                    default:
                        writer.WriteStartArray("criteria");
                        foreach (var level in question.Options)
                        {
                            WriteCriterion(writer, level.Criterion);
                        }

                        writer.WriteEndArray();
                        break;
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteContent(Utf8JsonWriter writer, DecisionContent content)
    {
        if (content.TryGetString(out var text))
        {
            writer.WriteStringValue(text);
        }
        else
        {
            Assert.True(content.TryGetJson(out var json));
            json.WriteTo(writer);
        }
    }

    private static void WriteCriterion(Utf8JsonWriter writer, Criterion? criterion)
    {
        if (criterion is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (criterion.Examples.Count == 0 && criterion.NotFor.Count == 0)
        {
            writer.WriteStringValue(criterion.Description);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("description", criterion.Description);
        WriteStrings(writer, "examples", criterion.Examples);
        WriteStrings(writer, "not_for", criterion.NotFor);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
