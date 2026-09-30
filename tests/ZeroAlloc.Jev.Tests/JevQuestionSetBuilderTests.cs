using System.Text;
using System.Text.Json.Nodes;

namespace ZeroAlloc.Jev.Tests;

/// <summary>Sets built at run time send the same questions as the fixtures the generated sets are checked against.</summary>
public sealed class JevQuestionSetBuilderTests
{
    [Fact]
    public void Noul_MatchesFixture()
        => AssertQuestions("request-noul.json", JevQuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _, c => c
                .WhenTrue("Explicitly time-sensitive")
                .WhenFalse("No urgency expressed")));

    [Fact]
    public void NoulWithoutCriteria_MatchesFixture()
        => AssertQuestions("request-noul-minimal.json", JevQuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _));

    [Fact]
    public void EnumChoice_WithAnUndescribedMember_MatchesFixture()
        => AssertQuestions("request-choice.json", JevQuestionSet.CreateBuilder()
            .Choice<Department>("department", "Which team should handle this?", out _, o => o
                .Describe(Department.Billing, "Payments, invoicing, refunds")
                .Describe(Department.Technical, "Bugs, outages, integrations")
                .Describe(Department.Sales, "Pricing, upgrades, new accounts")));

    [Fact]
    public void EnumScore_MatchesFixture()
        => AssertQuestions("request-score.json", JevQuestionSet.CreateBuilder()
            .Score<Frustration>("frustration", "How frustrated is the customer?", out _, l => l
                .Level(Frustration.Calm, "Calm")
                .Level(Frustration.Frustrated, "Frustrated")
                .Level(Frustration.VeryAngry, "Very angry")));

    [Fact]
    public void StructuredCriteria_MatchFixture()
        => AssertQuestions("request-structured-criteria.json", JevQuestionSet.CreateBuilder()
            .Choice<StructuredDepartment>("department", "Which team should handle this?", out _, o => o
                .Describe(StructuredDepartment.Billing, JevCriterion.Text("Payments, invoicing, refunds")
                    .WithExamples("I was charged twice")
                    .WithNotFor("How much is Pro?"))
                .Describe(StructuredDepartment.Technical, JevCriterion.Text("Bugs, outages, integrations").WithExamples("The API returns 500"))
                .Describe(StructuredDepartment.Sales, JevCriterion.Text("Pricing, upgrades, new accounts").WithExamples().WithNotFor()))
            .Score<StructuredSeverity>("severity", "How severe is this?", out _, l => l
                .Level(StructuredSeverity.Low, JevCriterion.Text("Cosmetic").WithNotFor("Data loss"))
                .Level(StructuredSeverity.High, "Blocks work")));

    [Fact]
    public void JsonInstructions_MatchFixture()
        => AssertQuestions("request-structured.json", JevQuestionSet.CreateBuilder()
            .Noul("is_duplicate", JevContent.FromUtf8Json("""
                {
                  "potential_duplicate": { "name": "John Smith", "location": "Oakland, California", "last_employer": "Google" },
                  "question": "Is the resume for the same person as `potential_duplicate`?"
                }
                """u8), out _));

    [Fact]
    public void KeyedChoiceAndScore_MatchFixture()
        => AssertQuestions("request-keyed.json", JevQuestionSet.CreateBuilder()
            .Choice("product", "Which product is `message` about?", out _, o => o
                .Option("pro-plan", "The Pro subscription")
                .Option("team-plan", JevCriterion.Text("The Team subscription").WithExamples("We have 12 seats"))
                .Option("other"))
            .Score("effort", "How much effort will this take?", out _, l => l.Level("Minutes").Level("Hours").Level("Days")));

    [Fact]
    public void EnumScore_SendsTheLevelsInCallOrder()
    {
        var built = Built(JevQuestionSet.CreateBuilder()
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
        var built = JevQuestionSet.CreateBuilder()
            .Score<Frustration>("missing", "How?", out _, l => l.Level(Frustration.Calm, "Calm").Level(Frustration.VeryAngry, "Very angry"))
            .Score<Priority>("aliased", "How?", out _, l => l.Level(Priority.Low, "Low").Level(Priority.High, "High").Level(Priority.Legacy, "Legacy"))
            .Score<Frustration>("none", "How?", out _)
            .Build();

        Assert.True(built.IsFailure);
        var failures = built.Error.Failures;
        Assert.Equal(5, failures.Count);
        Assert.Contains(failures, f => f is { Rule: "JEV104", QuestionKey: "missing" } && f.Message.Contains("'Frustrated'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "JEV106", QuestionKey: "aliased" } && f.Message.Contains("'Low'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "JEV104", QuestionKey: "none" } && f.Message.Contains("'Calm'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "JEV104", QuestionKey: "none" } && f.Message.Contains("'Frustrated'", StringComparison.Ordinal));
        Assert.Contains(failures, f => f is { Rule: "JEV104", QuestionKey: "none" } && f.Message.Contains("'VeryAngry'", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithBrokenRules_FailsWithInvalidQuestions()
    {
        var built = JevQuestionSet.CreateBuilder()
            .Choice("team", "Which team?", out _)
            .Noul(string.Empty, "Anything?", out _)
            .Build();

        Assert.True(built.IsFailure);
        Assert.Equal(JevErrorKind.InvalidQuestions, built.Error.Kind);
        Assert.Null(built.Error.StatusCode);
        Assert.Contains(built.Error.Failures, f => f is { Rule: "JEV001", QuestionKey: "team" });
        Assert.Contains(built.Error.Failures, f => f is { Rule: "JEV106", QuestionKey: "" });
    }

    [Fact]
    public void Build_WithAdvice_SucceedsWithWarnings()
    {
        var built = JevQuestionSet.CreateBuilder().Noul("q", "   ", out _).Build();

        Assert.True(built.IsSuccess);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var warning = Assert.Single(built.Value.Warnings);
#pragma warning restore HLQ005
        Assert.Equal(new JevQuestionFailure("JEV003", "q", warning.Message), warning);
    }

    [Fact]
    public void NullKey_ThrowsOnAllTenQuestionMethods()
    {
        var builder = JevQuestionSet.CreateBuilder();
        Action[] calls =
        [
            () => builder.Noul(null!, "x", out _),
            () => builder.Noul(null!, "x", out _, _ => { }),
            () => builder.Choice<Department>(null!, "x", out _),
            () => builder.Choice<Department>(null!, "x", out _, _ => { }),
            () => builder.Choice(null!, "x", out _),
            () => builder.Choice(null!, "x", out _, _ => { }),
            () => builder.Score<Frustration>(null!, "x", out _),
            () => builder.Score<Frustration>(null!, "x", out _, _ => { }),
            () => builder.Score(null!, "x", out _),
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
        var builder = JevQuestionSet.CreateBuilder();

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
        var builder = JevQuestionSet.CreateBuilder();

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
        var builder = JevQuestionSet.CreateBuilder().Noul("kept", "Kept?", out _);

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
        var builder = JevQuestionSet.CreateBuilder()
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
        var builder = JevQuestionSet.CreateBuilder().Noul("kept", "Kept?", out _);

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
        var warned = JevQuestionSet.CreateBuilder().Noul("q", "   ", out _).Build().Value;
        var failed = JevQuestionSet.CreateBuilder().Choice("team", "Which team?", out _).Build().Error;

        Assert.NotEmpty(warned.Warnings);
        Assert.IsNotType<JevQuestionFailure[]>(warned.Warnings);
        Assert.NotEmpty(failed.Failures);
        Assert.IsNotType<JevQuestionFailure[]>(failed.Failures);
    }

    [Fact]
    public void Build_TakesASnapshot()
    {
        var builder = JevQuestionSet.CreateBuilder().Noul("a", "A?", out _);
        var first = Built(builder);
        builder.Noul("b", "B?", out _);
        var second = Built(builder);

        Assert.Equal("""{"a":{"type":"noul","instructions":"A?"}}""", Encoding.ASCII.GetString(first.QuestionsUtf8));
        Assert.Equal(
            """{"a":{"type":"noul","instructions":"A?"},"b":{"type":"noul","instructions":"B?"}}""",
            Encoding.ASCII.GetString(second.QuestionsUtf8));
    }

    private static void AssertQuestions(string fixture, JevQuestionSetBuilder builder)
    {
        var expected = Fixture.Load(fixture)["questions"];
        var actual = JsonNode.Parse(Built(builder).QuestionsUtf8);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"Expected {expected?.ToJsonString()} but built {actual?.ToJsonString()}.");
    }

    private static JevQuestionSet Built(JevQuestionSetBuilder builder)
    {
        var built = builder.Build();
        Assert.True(built.IsSuccess, built.IsFailure ? built.Error.ToString() : null);
        return built.Value;
    }
}
