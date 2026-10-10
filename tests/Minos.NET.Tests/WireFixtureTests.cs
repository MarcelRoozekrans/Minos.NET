using System.Runtime.CompilerServices;
using System.Text;
using Minos.Tests.WireFixtureSets;

namespace Minos.Tests;

internal static class WireFixtures
{
    // Capture mode overwrites the files and passes by design; it is only for writing them once.
    // Set MINOS_CAPTURE_WIRE_FIXTURES=1 once, on main's behaviour, to write the files; every other run compares.
    public static bool Capturing => string.Equals(Environment.GetEnvironmentVariable("MINOS_CAPTURE_WIRE_FIXTURES"), "1", StringComparison.Ordinal);

    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "WireFixtures", name + ".json"));

    public static void Write(string name, ReadOnlySpan<byte> bytes, [CallerFilePath] string caller = "")
        => File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(caller)!, "WireFixtures", name + ".json"), bytes.ToArray());

    public static void AssertMatches(string name, ReadOnlySpan<byte> actual)
    {
        if (Capturing)
        {
            Write(name, actual);
            return;
        }

        var expected = Read(name);
        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
        Assert.True(actual.SequenceEqual(expected));
    }
}

public sealed class WireFixtureTests
{
    public static TheoryData<string> GeneratedCases => ["NoulOnly", "ChoiceOnly", "ScoreOnly", "Mixed", "Structured", "KeywordMembers", "WithState", "Escapes"];

    internal static readonly Dictionary<string, Func<QuestionSet>> BuiltCases = new()
    {
        ["Differential"] = BuilderGeneratorDifferentialTests.Built,
        ["Noul"] = BuiltWireCases.Noul,
        ["NoulWithOnlyWhenTrue"] = BuiltWireCases.NoulWithOnlyWhenTrue,
        ["NoulWithoutCriteria"] = BuiltWireCases.NoulWithoutCriteria,
        ["EnumChoiceWithAnUndescribedMember"] = BuiltWireCases.EnumChoiceWithAnUndescribedMember,
        ["EnumScore"] = BuiltWireCases.EnumScore,
        ["EnumScoreInCallOrder"] = BuiltWireCases.EnumScoreInCallOrder,
        ["StructuredCriteria"] = BuiltWireCases.StructuredCriteria,
        ["JsonInstructions"] = BuiltWireCases.JsonInstructions,
        ["JsonCriterion"] = BuiltWireCases.JsonCriterion,
        ["JsonAtDepthLimit"] = BuiltWireCases.JsonAtDepthLimit,
        ["KeyedChoiceAndScore"] = BuiltWireCases.KeyedChoiceAndScore,
        ["Escapes"] = BuiltWireCases.Escapes,
    };

    public static TheoryData<string> BuiltCaseNames => [.. BuiltCases.Keys];

    [Theory]
    [MemberData(nameof(GeneratedCases))]
    public void Generated_QuestionsMatchTheFixture(string name)
        => WireFixtures.AssertMatches("generated-" + name, GeneratedQuestions(name));

    [Theory]
    [MemberData(nameof(BuiltCaseNames))]
    public void Built_QuestionsMatchTheFixture(string name)
        => WireFixtures.AssertMatches("built-" + name, BuiltCases[name]().QuestionsUtf8);

    [Theory]
    [MemberData(nameof(BuiltCaseNames))]
    public void Built_ProtocolQuestionsMatchTheFixture(string name)
        => WireFixtures.AssertMatches("built-" + name, Minos.Protocols.SystemOneProtocol.QuestionsUtf8(BuiltCases[name]().Definition));

    [Theory]
    [MemberData(nameof(BuiltCaseNames))]
    public void Built_DefinitionOptionKeysResolveToTheSamePositionsInTheAnswerPath(string name)
    {
        var set = BuiltCases[name]();
        var questions = set.Definition.Questions;
        for (var q = 0; q < questions.Count; q++)
        {
            if (questions[q].Kind == QuestionKind.Noul)
            {
                continue;
            }

            var options = set.Plan[q].Options!;
            Assert.Equal(questions[q].Options.Count, options.Count);
            for (var i = 0; i < questions[q].Options.Count; i++)
            {
                var reader = new System.Text.Json.Utf8JsonReader(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(questions[q].Options[i].Key)));
                reader.Read();
                Assert.Equal(i, options.IndexOfKey(ref reader));
            }
        }
    }

    [Theory]
    [MemberData(nameof(GeneratedCases))]
    public void Generated_ProtocolQuestionsMatchTheFixture(string name)
        => WireFixtures.AssertMatches("generated-" + name, Minos.Protocols.SystemOneProtocol.QuestionsUtf8(GeneratedDefinition(name)));

    internal static QuestionSetDefinition GeneratedDefinition(string name) => name switch
    {
        "NoulOnly" => WfNoulOnly.Definition,
        "ChoiceOnly" => WfChoiceOnly.Definition,
        "ScoreOnly" => WfScoreOnly.Definition,
        "Mixed" => WfMixed.Definition,
        "Structured" => WfStructured.Definition,
        "KeywordMembers" => WfKeywordMembers.Definition,
        "WithState" => WfWithState.Definition,
        "Escapes" => WfEscapes.Definition,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    // The generated literal, which stays until the generator stops emitting QuestionsUtf8.
    internal static ReadOnlySpan<byte> GeneratedQuestions(string name) => name switch
    {
        "NoulOnly" => WfNoulOnly.QuestionsUtf8,
        "ChoiceOnly" => WfChoiceOnly.QuestionsUtf8,
        "ScoreOnly" => WfScoreOnly.QuestionsUtf8,
        "Mixed" => WfMixed.QuestionsUtf8,
        "Structured" => WfStructured.QuestionsUtf8,
        "KeywordMembers" => WfKeywordMembers.QuestionsUtf8,
        "WithState" => WfWithState.QuestionsUtf8,
        "Escapes" => WfEscapes.QuestionsUtf8,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}

/// <summary>Every builder chain whose questions are pinned as a fixture, declared once and shared with the tests that assert on it.</summary>
internal static class BuiltWireCases
{
    public static QuestionSet Noul() => Built(QuestionSet.CreateBuilder()
        .Noul("is_urgent", "Does this convey urgency?", out _, c => c
            .WhenTrue("Explicitly time-sensitive")
            .WhenFalse("No urgency expressed")));

    public static QuestionSet NoulWithOnlyWhenTrue() => Built(QuestionSet.CreateBuilder()
        .Noul("is_urgent", "Does this convey urgency?", out _, c => c
            .WhenTrue("Explicitly time-sensitive")));

    public static QuestionSet NoulWithoutCriteria() => Built(QuestionSet.CreateBuilder()
        .Noul("is_urgent", "Does this convey urgency?", out _));

    public static QuestionSet EnumChoiceWithAnUndescribedMember() => Built(QuestionSet.CreateBuilder()
        .Choice<Department>("department", "Which team should handle this?", out _, o => o
            .Describe(Department.Billing, "Payments, invoicing, refunds")
            .Describe(Department.Technical, "Bugs, outages, integrations")
            .Describe(Department.Sales, "Pricing, upgrades, new accounts")));

    public static QuestionSet EnumScore() => Built(QuestionSet.CreateBuilder()
        .Score<Frustration>("frustration", "How frustrated is the customer?", out _, l => l
            .Level(Frustration.Calm, "Calm")
            .Level(Frustration.Frustrated, "Frustrated")
            .Level(Frustration.VeryAngry, "Very angry")));

    public static QuestionSet EnumScoreInCallOrder() => Built(QuestionSet.CreateBuilder()
        .Score<Frustration>("mood", "How?", out _, l => l
            .Level(Frustration.VeryAngry, "Very angry")
            .Level(Frustration.Calm, "Calm")
            .Level(Frustration.Frustrated, "Frustrated")));

    public static QuestionSet StructuredCriteria() => Built(QuestionSet.CreateBuilder()
        .Choice<StructuredDepartment>("department", "Which team should handle this?", out _, o => o
            .Describe(StructuredDepartment.Billing, Criterion.Text("Payments, invoicing, refunds")
                .WithExamples("I was charged twice")
                .WithNotFor("How much is Pro?"))
            .Describe(StructuredDepartment.Technical, Criterion.Text("Bugs, outages, integrations").WithExamples("The API returns 500"))
            .Describe(StructuredDepartment.Sales, Criterion.Text("Pricing, upgrades, new accounts").WithExamples().WithNotFor()))
        .Score<StructuredSeverity>("severity", "How severe is this?", out _, l => l
            .Level(StructuredSeverity.Low, Criterion.Text("Cosmetic").WithNotFor("Data loss"))
            .Level(StructuredSeverity.High, "Blocks work")));

    public static QuestionSet JsonInstructions() => Built(QuestionSet.CreateBuilder()
        .Noul("is_duplicate", DecisionContent.FromUtf8Json("""
            {
              "potential_duplicate": { "name": "John Smith", "location": "Oakland, California", "last_employer": "Google" },
              "question": "Is the resume for the same person as `potential_duplicate`?"
            }
            """u8), out _));

    public static QuestionSet JsonCriterion() => Built(QuestionSet.CreateBuilder()
        .Choice<Department>("department", "Which team should handle this?", out _, o => o
            .Describe(Department.Billing, Criterion.Json(DecisionContent.FromUtf8Json("""{"handles":["charges","refunds"],"note":"Say \"billing\""}"""u8)))
            .Describe(Department.Technical, "Bugs, outages, integrations")
            .Describe(Department.Sales, Criterion.Text("Pricing, upgrades, new accounts"))));

    public static QuestionSet JsonAtDepthLimit()
    {
        var deep = DecisionContent.FromUtf8Json(Encoding.UTF8.GetBytes(DeepJson.Text));
        return Built(QuestionSet.CreateBuilder()
            .Noul("is_deep", deep, out _)
            .Choice<DeepOption>("depth", "How deep?", out _, o => o
                .Describe(DeepOption.Shallow, Criterion.Json(deep))
                .Describe(DeepOption.Deep, Criterion.Json(deep))));
    }

    public static QuestionSet KeyedChoiceAndScore() => Built(QuestionSet.CreateBuilder()
        .Choice("product", "Which product is `message` about?", out _, o => o
            .Option("pro-plan", "The Pro subscription")
            .Option("team-plan", Criterion.Text("The Team subscription").WithExamples("We have 12 seats"))
            .Option("other"))
        .Score("effort", "How much effort will this take?", out _, l => l.Level("Minutes").Level("Hours").Level("Days")));

    // Characters the encoder escapes or passes through, and a lone surrogate built in code: xUnit theory data would mangle it.
    public static QuestionSet Escapes()
    {
        var text = "< > & ' + / \r \t \u007F \u2028 \u0085 " + new string((char)0xD800, 1) + " end";
        var json = DecisionContent.FromUtf8Json("""{"q":"café \u00e9 \u003c < \u0026 &"}"""u8);
        return Built(QuestionSet.CreateBuilder()
            .Noul("n " + text, "n " + text, out _, c => c.WhenTrue("t " + text).WhenFalse("f " + text))
            .Noul("json_instructions", json, out _)
            .Choice("c " + text, "c " + text, out _, o => o
                .Option("k " + text, "o " + text)
                .Option("json_criterion", Criterion.Json(json))
                .Option("other"))
            .Score("s " + text, "s " + text, out _, l => l.Level("l " + text).Level(Criterion.Json(json))));
    }

    public static QuestionSet Built(QuestionSetBuilder builder)
    {
        var built = builder.Build();
        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.ToString() : null);
        return built.Value;
    }
}
