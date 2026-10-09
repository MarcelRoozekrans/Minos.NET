using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Serialization;

namespace Minos.Tests;

/// <summary>Sets built at run time send the same questions as the fixtures the generated sets are checked against.</summary>
public sealed class QuestionSetBuilderTests
{
    [Fact]
    public void Noul_MatchesFixture()
        => AssertQuestions("request-noul.json", QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _, c => c
                .WhenTrue("Explicitly time-sensitive")
                .WhenFalse("No urgency expressed")));

    [Fact]
    public void NoulWithoutCriteria_MatchesFixture()
        => AssertQuestions("request-noul-minimal.json", QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _));

    [Fact]
    public void EnumChoice_WithAnUndescribedMember_MatchesFixture()
        => AssertQuestions("request-choice.json", QuestionSet.CreateBuilder()
            .Choice<Department>("department", "Which team should handle this?", out _, o => o
                .Describe(Department.Billing, "Payments, invoicing, refunds")
                .Describe(Department.Technical, "Bugs, outages, integrations")
                .Describe(Department.Sales, "Pricing, upgrades, new accounts")));

    [Fact]
    public void EnumScore_MatchesFixture()
        => AssertQuestions("request-score.json", QuestionSet.CreateBuilder()
            .Score<Frustration>("frustration", "How frustrated is the customer?", out _, l => l
                .Level(Frustration.Calm, "Calm")
                .Level(Frustration.Frustrated, "Frustrated")
                .Level(Frustration.VeryAngry, "Very angry")));

    [Fact]
    public void StructuredCriteria_MatchFixture()
        => AssertQuestions("request-structured-criteria.json", QuestionSet.CreateBuilder()
            .Choice<StructuredDepartment>("department", "Which team should handle this?", out _, o => o
                .Describe(StructuredDepartment.Billing, Criterion.Text("Payments, invoicing, refunds")
                    .WithExamples("I was charged twice")
                    .WithNotFor("How much is Pro?"))
                .Describe(StructuredDepartment.Technical, Criterion.Text("Bugs, outages, integrations").WithExamples("The API returns 500"))
                .Describe(StructuredDepartment.Sales, Criterion.Text("Pricing, upgrades, new accounts").WithExamples().WithNotFor()))
            .Score<StructuredSeverity>("severity", "How severe is this?", out _, l => l
                .Level(StructuredSeverity.Low, Criterion.Text("Cosmetic").WithNotFor("Data loss"))
                .Level(StructuredSeverity.High, "Blocks work")));

    [Fact]
    public void JsonInstructions_MatchFixture()
        => AssertQuestions("request-structured.json", QuestionSet.CreateBuilder()
            .Noul("is_duplicate", DecisionContent.FromUtf8Json("""
                {
                  "potential_duplicate": { "name": "John Smith", "location": "Oakland, California", "last_employer": "Google" },
                  "question": "Is the resume for the same person as `potential_duplicate`?"
                }
                """u8), out _));

    [Fact]
    public void JsonAtDepthLimit_DeserializesThroughWireModel()
    {
        var deep = DecisionContent.FromUtf8Json(Encoding.UTF8.GetBytes(DeepJson.Text));
        var built = Built(QuestionSet.CreateBuilder()
            .Noul("is_deep", deep, out _)
            .Choice<DeepOption>("depth", "How deep?", out _, o => o
                .Describe(DeepOption.Shallow, Criterion.Json(deep))
                .Describe(DeepOption.Deep, Criterion.Json(deep))));
        var json = "{\"state\":\"x\",\"questions\":" + Encoding.UTF8.GetString(built.QuestionsUtf8) + "}";
        Assert.Equal(64, MaxNesting(json));

        var request = JsonSerializer.Deserialize(json, DecisionJsonContext.Default.SystemOneRequest)!;

        Assert.True(request.Questions["is_deep"].Instructions.TryGetJson(out var instructions));
        Assert.Equal(JsonValueKind.Array, instructions.ValueKind);
        var depth = Assert.IsType<ChoiceQuestion>(request.Questions["depth"]);
        Assert.True(depth.Criteria["deep"]!.Value.TryGetJson(out var criterion));
        Assert.Equal(JsonValueKind.Array, criterion.ValueKind);

        static int MaxNesting(string text)
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(text), new JsonReaderOptions { MaxDepth = 1000 });
            var max = 0;
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    max = Math.Max(max, reader.CurrentDepth + 1);
                }
            }

            return max;
        }
    }

    [Fact]
    public void KeyedChoiceAndScore_MatchFixture()
        => AssertQuestions("request-keyed.json", QuestionSet.CreateBuilder()
            .Choice("product", "Which product is `message` about?", out _, o => o
                .Option("pro-plan", "The Pro subscription")
                .Option("team-plan", Criterion.Text("The Team subscription").WithExamples("We have 12 seats"))
                .Option("other"))
            .Score("effort", "How much effort will this take?", out _, l => l.Level("Minutes").Level("Hours").Level("Days")));

    [Fact]
    public void EnumScore_SendsTheLevelsInCallOrder()
    {
        var built = Built(QuestionSet.CreateBuilder()
            .Score<Frustration>("mood", "How?", out _, l => l
                .Level(Frustration.VeryAngry, "Very angry")
                .Level(Frustration.Calm, "Calm")
                .Level(Frustration.Frustrated, "Frustrated")));

        Assert.Equal(
            """{"mood":{"type":"score","instructions":"How?","criteria":["Very angry","Calm","Frustrated"]}}""",
            Encoding.ASCII.GetString(built.QuestionsUtf8));
    }

    [Fact]
    public void EnumScore_WithAMemberMissingOrGivenTwice_FailsPerMember()
    {
        // Priority declares Low = 10, High = 20, Legacy = 10: Legacy is Low, so giving both gives Low twice.
        var built = QuestionSet.CreateBuilder()
            .Score<Frustration>("missing", "How?", out _, l => l.Level(Frustration.Calm, "Calm").Level(Frustration.VeryAngry, "Very angry"))
            .Score<Priority>("aliased", "How?", out _, l => l.Level(Priority.Low, "Low").Level(Priority.High, "High").Level(Priority.Legacy, "Legacy"))
            .Score<Frustration>("none", "How?", out _, _ => { })
            .Build();

        Assert.True(built.IsFailure);
        var failures = built.Error.Failures;
        Assert.Equal(5, failures.Count);
        Assert.Contains(failures, f => f is { Rule: "MIN104", QuestionKey: "missing" } && f.Message.Contains("'Frustrated'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "MIN106", QuestionKey: "aliased" } && f.Message.Contains("'Low'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "MIN104", QuestionKey: "none" } && f.Message.Contains("'Calm'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "MIN104", QuestionKey: "none" } && f.Message.Contains("'Frustrated'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "MIN104", QuestionKey: "none" } && f.Message.Contains("'VeryAngry'", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithBrokenRules_FailsWithInvalidQuestions()
    {
        var built = QuestionSet.CreateBuilder()
            .Choice("team", "Which team?", out _, _ => { })
            .Noul(string.Empty, "Anything?", out _)
            .Build();

        Assert.True(built.IsFailure);
        Assert.Equal(DecisionErrorKind.InvalidQuestions, built.Error.Kind);
        Assert.Null(built.Error.StatusCode);
        Assert.Contains(built.Error.Failures, f => f is { Rule: "MIN001", QuestionKey: "team" });
        Assert.Contains(built.Error.Failures, f => f is { Rule: "MIN106", QuestionKey: "" });
    }

    [Fact]
    public void Build_WithAdvice_SucceedsWithWarnings()
    {
        var built = QuestionSet.CreateBuilder().Noul("q", "   ", out _).Build();

        Assert.True(built.IsSuccess);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var warning = Assert.Single(built.Value.Warnings);
#pragma warning restore HLQ005
        Assert.Equal(new QuestionFailure("MIN003", "q", warning.Message), warning);
    }

    [Fact]
    public void OnlyNoulAndEnumChoice_HaveAnOverloadWithoutAConfigurator()
    {
        // A keyed Choice, a keyed Score and an enum Score with nothing configured always fail Build, so none has an overload
        // that lacks the configurator: the mistake is a compile error instead of a run-time MIN001, MIN002 or MIN104.
        var withoutConfigurator = typeof(QuestionSetBuilder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetParameters().Length == 3)
            .Select(method => method.Name + (method.IsGenericMethodDefinition ? "<T>" : string.Empty))
            .Order(StringComparer.Ordinal);

        Assert.Equal(["Choice<T>", "Noul"], withoutConfigurator);
    }

    [Fact]
    public void NullKey_ThrowsOnAllSevenQuestionMethods()
    {
        var builder = QuestionSet.CreateBuilder();
        Action[] calls =
        [
            () => builder.Noul(null!, "x", out _),
            () => builder.Noul(null!, "x", out _, _ => { }),
            () => builder.Choice<Department>(null!, "x", out _),
            () => builder.Choice<Department>(null!, "x", out _, _ => { }),
            () => builder.Choice(null!, "x", out _, _ => { }),
            () => builder.Score<Frustration>(null!, "x", out _, _ => { }),
            () => builder.Score(null!, "x", out _, _ => { }),
        ];

        foreach (var call in calls)
        {
            Assert.Equal("key", Assert.Throws<ArgumentNullException>(call).ParamName);
        }
    }

    [Fact]
    public void NullArguments_ThrowArgumentNullException()
    {
        var builder = QuestionSet.CreateBuilder();

        Assert.Equal("text", Assert.Throws<ArgumentNullException>(() => builder.Noul("k", (string)null!, out _)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.Noul("k", "x", out _, null!)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.Choice<Department>("k", "x", out _, null!)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.Choice("k", "x", out _, null!)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.Score<Frustration>("k", "x", out _, null!)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.Score("k", "x", out _, null!)).ParamName);
        Assert.Equal("criterion", Assert.Throws<ArgumentNullException>(
            () => builder.Choice<Department>("k", "x", out _, o => o.Describe(Department.Billing, null!))).ParamName);
        Assert.Equal("criterion", Assert.Throws<ArgumentNullException>(
            () => builder.Score<Frustration>("k", "x", out _, l => l.Level(Frustration.Calm, null!))).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => builder.Choice("k", "x", out _, o => o.Option(null!))).ParamName);
        Assert.Equal("criterion", Assert.Throws<ArgumentNullException>(() => builder.Choice("k", "x", out _, o => o.Option("a", null!))).ParamName);
        Assert.Equal("criterion", Assert.Throws<ArgumentNullException>(() => builder.Score("k", "x", out _, l => l.Level(null!))).ParamName);
    }

    [Fact]
    public void InvalidValues_ThrowArgumentException()
    {
        var builder = QuestionSet.CreateBuilder();

        Assert.Equal("instructions", Assert.Throws<ArgumentException>(() => builder.Noul("k", default, out _)).ParamName);
        Assert.Equal("description", Assert.Throws<ArgumentException>(() => builder.Noul("k", "x", out _, c => c.WhenTrue(default))).ParamName);
        Assert.Equal("option", Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.Choice<Department>("k", "x", out _, o => o.Describe((Department)99, "x"))).ParamName);
        Assert.Equal("level", Assert.Throws<ArgumentOutOfRangeException>(
            () => builder.Score<Frustration>("k", "x", out _, l => l.Level((Frustration)99, "x"))).ParamName);
    }

    [Fact]
    public void AFailedCall_AddsNoQuestion()
    {
        var builder = QuestionSet.CreateBuilder().Noul("kept", "Kept?", out _);

        Action[] calls =
        [
            () => builder.Noul("dropped", "Dropped?", out _, _ => throw new InvalidOperationException("configure failed")),
            () => builder.Choice<Department>("dropped", "Dropped?", out _, _ => throw new InvalidOperationException("configure failed")),
            () => builder.Choice("dropped", "Dropped?", out _, _ => throw new InvalidOperationException("configure failed")),
            () => builder.Score<Frustration>("dropped", "Dropped?", out _, _ => throw new InvalidOperationException("configure failed")),
            () => builder.Score("dropped", "Dropped?", out _, _ => throw new InvalidOperationException("configure failed")),
        ];

        foreach (var call in calls)
        {
            Assert.Equal("configure failed", Assert.Throws<InvalidOperationException>(call).Message);
        }

        Assert.Equal("""{"kept":{"type":"noul","instructions":"Kept?"}}""", Encoding.ASCII.GetString(Built(builder).QuestionsUtf8));
    }

    [Fact]
    public void AConfigurator_ThrowsOnceItsCallbackReturned_AndChangesNothing()
    {
        NoulCriteriaBuilder? noul = null;
        ChoiceOptionsBuilder<Department>? choice = null;
        ScoreLevelsBuilder<Frustration>? score = null;
        KeyedChoiceOptionsBuilder? keyedChoice = null;
        KeyedScoreLevelsBuilder? keyedScore = null;
        var builder = QuestionSet.CreateBuilder()
            .Noul("n", "N?", out _, c => noul = c)
            .Choice<Department>("c", "C?", out _, o => choice = o)
            .Score<Frustration>("s", "S?", out _, l => score = l.Level(Frustration.Calm, "a").Level(Frustration.Frustrated, "b").Level(Frustration.VeryAngry, "c"))
            .Choice("kc", "KC?", out _, o => keyedChoice = o.Option("a"))
            .Score("ks", "KS?", out _, l => keyedScore = l.Level("a"));
        var before = Built(builder).QuestionsUtf8.ToArray();

        Assert.Throws<InvalidOperationException>(() => noul!.WhenTrue("x"));
        Assert.Throws<InvalidOperationException>(() => noul!.WhenFalse("x"));
        Assert.Throws<InvalidOperationException>(() => choice!.Describe(Department.Billing, "x"));
        Assert.Throws<InvalidOperationException>(() => score!.Level(Frustration.Calm, "x"));
        Assert.Throws<InvalidOperationException>(() => keyedChoice!.Option("b"));
        Assert.Throws<InvalidOperationException>(() => keyedChoice!.Option("b", "x"));
        Assert.Throws<InvalidOperationException>(() => keyedScore!.Level("x"));

        Assert.Equal(before, Built(builder).QuestionsUtf8.ToArray());
    }

    [Fact]
    public void AConfigurator_ThatThrew_IsClosedToo()
    {
        KeyedChoiceOptionsBuilder? stored = null;
        var builder = QuestionSet.CreateBuilder().Noul("kept", "Kept?", out _);

        Assert.Throws<InvalidOperationException>(() => builder.Choice("dropped", "D?", out _, o =>
        {
            stored = o;
            throw new InvalidOperationException("configure failed");
        }));

        Assert.Throws<InvalidOperationException>(() => stored!.Option("late"));
        Assert.Equal("""{"kept":{"type":"noul","instructions":"Kept?"}}""", Encoding.ASCII.GetString(Built(builder).QuestionsUtf8));
    }

    [Fact]
    public void WarningsAndFailures_AreNotWritableArrays()
    {
        var warned = QuestionSet.CreateBuilder().Noul("q", "   ", out _).Build().Value;
        var failed = QuestionSet.CreateBuilder().Choice("team", "Which team?", out _, _ => { }).Build().Error;

        Assert.NotEmpty(warned.Warnings);
        Assert.IsNotType<QuestionFailure[]>(warned.Warnings);
        Assert.NotEmpty(failed.Failures);
        Assert.IsNotType<QuestionFailure[]>(failed.Failures);
    }

    [Fact]
    public void EmptyWarningsAndFailures_AreSharedAndReadOnly()
    {
        var first = Built(QuestionSet.CreateBuilder().Noul("a", "A?", out _));
        var second = Built(QuestionSet.CreateBuilder().Noul("b", "B?", out _));
        var other = new DecisionError(DecisionErrorKind.Timeout, "The request timed out.");

        Assert.Empty(first.Warnings);
        Assert.Same(first.Warnings, second.Warnings);
        Assert.True(((ICollection<QuestionFailure>)first.Warnings).IsReadOnly);
        Assert.Empty(other.Failures);
        Assert.True(((ICollection<QuestionFailure>)other.Failures).IsReadOnly);
    }

    [Fact]
    public void Build_TakesASnapshot()
    {
        var builder = QuestionSet.CreateBuilder().Noul("a", "A?", out _);
        var first = Built(builder);
        builder.Noul("b", "B?", out _);
        var second = Built(builder);

        Assert.Equal("""{"a":{"type":"noul","instructions":"A?"}}""", Encoding.ASCII.GetString(first.QuestionsUtf8));
        Assert.Equal(
            """{"a":{"type":"noul","instructions":"A?"},"b":{"type":"noul","instructions":"B?"}}""",
            Encoding.ASCII.GetString(second.QuestionsUtf8));
    }

    private static void AssertQuestions(string fixture, QuestionSetBuilder builder)
    {
        var expected = Fixture.Load(fixture)["questions"];
        var actual = JsonNode.Parse(Built(builder).QuestionsUtf8);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected {expected?.ToJsonString()} but built {actual?.ToJsonString()}.");
    }

    private static QuestionSet Built(QuestionSetBuilder builder)
    {
        var built = builder.Build();
        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.ToString() : null);
        return built.Value;
    }
}
