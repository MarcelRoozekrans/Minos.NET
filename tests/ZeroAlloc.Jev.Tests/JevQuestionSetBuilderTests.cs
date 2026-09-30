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
    public void NullArguments_ThrowArgumentNullException()
    {
        var builder = JevQuestionSet.CreateBuilder();

        Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => builder.Noul(null!, "x", out _)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => builder.Choice<Department>(null!, "x", out _)).ParamName);
        Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => builder.Score(null!, "x", out _)).ParamName);
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

        Assert.Throws<InvalidOperationException>(
            () => builder.Choice("dropped", "Dropped?", out _, _ => throw new InvalidOperationException("configure failed")));

        Assert.Equal("""{"kept":{"type":"noul","instructions":"Kept?"}}""", Encoding.ASCII.GetString(Built(builder).QuestionsUtf8));
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
