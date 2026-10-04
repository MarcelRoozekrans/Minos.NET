using System.Text.Json;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class QuestionSetsAtRunTimeTests
{
    // Priority: 0 x 0.1 + 1 x 0.2 + 2 x 0.7 = 1.6.
    private const string RouteResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "urgent": { "type": "noul", "noul": 0.88 },
            "team": { "type": "choice", "choice": "payments", "probabilities": { "payments": 0.75, "platform": 0.25 }, "confidence": 0.7 },
            "priority": { "type": "score", "score": 1.6, "legend": { "0": "Can wait", "1": "This week", "2": "Today" }, "probabilities": { "0": 0.1, "1": 0.2, "2": 0.7 }, "confidence": 0.8 }
          },
          "usage": { "input_tokens": 80, "output_tokens": 9 }
        }
        """;

    private static readonly (string Key, string Summary)[] Teams = [("payments", "Billing and refunds"), ("platform", "Outages and APIs")];

    private enum Route
    {
        SendToBilling,
        Billing = SendToBilling,
        NeedsHuman,
    }

    [Fact]
    public async Task RouteAsync_BuildsOnce_EvaluatesAndReadsThroughHandles()
    {
        var router = new TenantRouter(Teams);
        var (http, jev, requests) = CannedJev.Client(RouteResponse);
        using (http)
        using (jev)
        {
            var routed = await router.RouteAsync(jev, "Our checkout is down.", CancellationToken.None);

            Assert.Equal((true, "payments", Priority.High), routed);
            Assert.Equal((true, "payments", Priority.High), await router.RouteAsync(jev, "Again.", CancellationToken.None));
        }

        Assert.Equal(2, requests.Count);
        using var request = JsonDocument.Parse(requests[0]);
        Assert.Equal("Our checkout is down.", request.RootElement.GetProperty("state").GetString());
        var questions = request.RootElement.GetProperty("questions");
        Assert.Equal("The sender needs an answer today", questions.GetProperty("urgent").GetProperty("criteria").GetProperty("true").GetString());
        Assert.Equal("Billing and refunds", questions.GetProperty("team").GetProperty("criteria").GetProperty("payments").GetString());
        Assert.Equal("This week", questions.GetProperty("priority").GetProperty("criteria")[1].GetString());
    }

    [Fact]
    public async Task RouteAsync_ReportsAFailureAsNull()
    {
        var router = new TenantRouter(Teams);
        var (http, jev, _) = CannedJev.Client("this is not json");
        using (http)
        using (jev)
        {
            Assert.Null(await router.RouteAsync(jev, "Anything.", CancellationToken.None));
        }
    }

    [Fact]
    public void TenantRouter_ReportsEveryBrokenRuleOfATenantsTeams()
    {
        var empty = Assert.Throws<InvalidOperationException>(() => new TenantRouter([]));
        Assert.Contains("JEV001", empty.Message, StringComparison.Ordinal);

        var duplicate = Assert.Throws<InvalidOperationException>(() => new TenantRouter([("a", "One"), ("a", "Two"), ("", "None")]));
        Assert.Contains("JEV106", duplicate.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", duplicate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Criteria_AreSentAsTextOrCriterionObjectsOrJson_AndReadWithTheEnum()
    {
        var (set, team) = CriteriaExample.Create();

        using var actual = JsonDocument.Parse(set.QuestionsUtf8.ToArray());
        using var expected = JsonDocument.Parse("""
            {
              "team": {
                "type": "choice",
                "instructions": "Which team should handle this?",
                "criteria": {
                  "billing": {
                    "description": "Payments, invoices and refunds",
                    "examples": ["I was charged twice"],
                    "not_for": ["How much is the Pro plan?"]
                  },
                  "technical": { "scope": "bugs", "also": ["outages", "integrations"] },
                  "sales": "Pricing and upgrades"
                }
              }
            }
            """);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));

        var (http, jev, _) = CannedJev.Client("""
            { "model": "m", "answers": { "team": { "type": "choice", "choice": "technical", "probabilities": { "billing": 0.1, "technical": 0.8, "sales": 0.1 }, "confidence": 0.9 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync(set, "Help", CancellationToken.None);

            Assert.True(result.IsSuccess);
            var answer = result.Value.Get(team);
            Assert.Equal(ServiceTeam.Technical, answer.Value);
            Assert.Equal(0.8, answer.Probabilities[ServiceTeam.Technical]);
        }
    }

    [Fact]
    public void Build_ListsEveryBrokenRule_UnderTheInvalidQuestionsKind()
    {
        Assert.Equal(["JEV001 team", "JEV106 team"], RuleChecks.BrokenRules());

        var built = JevQuestionSet.CreateBuilder().Noul("a", "A?", out NoulHandle _).Noul("a", "B?", out NoulHandle _).Build();
        Assert.True(built.IsFailure);
        Assert.Equal(JevErrorKind.InvalidQuestions, built.Error.Kind);
        Assert.Collection(built.Error.Failures, failure => Assert.Equal(("JEV106", "a"), (failure.Rule, failure.QuestionKey)));
    }

    [Fact]
    public void Build_AWarningStillBuildsTheSet()
    {
        Assert.Equal(["JEV003 urgent", "JEV005 mood"], RuleChecks.Advice());

        var clean = JevQuestionSet.CreateBuilder().Noul("a", "A?", out NoulHandle _).Build();
        Assert.True(clean.IsSuccess);
        Assert.Empty(clean.Value.Warnings);
    }

    [Fact]
    public void Build_ChecksTheRulesInTheTable()
    {
        // JEV104: a member of an enum Score that is not given a level, and JEV106: one given twice.
        var missing = JevQuestionSet.CreateBuilder()
            .Score("p", "How soon?", out ScoreHandle<Priority> _, levels => levels.Level(Priority.Low, "x").Level(Priority.Medium, "y"))
            .Build();
        Assert.Equal(["JEV104"], Rules(missing.Error));

        var twice = JevQuestionSet.CreateBuilder()
            .Score("p", "How soon?", out ScoreHandle<Priority> _, levels => levels
                .Level(Priority.Low, "x").Level(Priority.Low, "x").Level(Priority.Medium, "y").Level(Priority.High, "z"))
            .Build();
        Assert.Equal(["JEV106"], Rules(twice.Error));

        // JEV001 and JEV002: a keyed Score with no levels. An enum Choice with no options cannot happen, as every member is one.
        var noLevels = JevQuestionSet.CreateBuilder().Score("s", "How?", out KeyedScoreHandle _, levels => { }).Build();
        Assert.Equal(["JEV002"], Rules(noLevels.Error));

        // An empty question key, an empty option key and a repeated option key are JEV106.
        var keys = JevQuestionSet.CreateBuilder()
            .Noul(string.Empty, "A?", out NoulHandle _)
            .Choice("c", "Which?", out KeyedChoiceHandle _, o => o.Option("x").Option("x").Option(string.Empty))
            .Build();
        Assert.Equal(["JEV106", "JEV106", "JEV106"], Rules(keys.Error));

        // JEV108: JSON nested deeper than 60 levels, and 60 levels are fine.
        var deep = JevContent.FromUtf8Json(System.Text.Encoding.UTF8.GetBytes(new string('[', 61) + new string(']', 61)));
        var deepest = JevContent.FromUtf8Json(System.Text.Encoding.UTF8.GetBytes(new string('[', 60) + new string(']', 60)));
        Assert.Equal(["JEV108"], Rules(JevQuestionSet.CreateBuilder().Noul("n", deep, out NoulHandle _).Build().Error));
        Assert.True(JevQuestionSet.CreateBuilder().Noul("n", deepest, out NoulHandle _).Build().IsSuccess);

        // JEV003: a blank description or example, and JSON that is exactly {} or [].
        var blank = JevQuestionSet.CreateBuilder()
            .Choice("c", "Which?", out KeyedChoiceHandle _, o => o.Option("x", JevCriterion.Text("ok").WithExamples(" ")))
            .Noul("n", JevContent.FromUtf8Json("{}"u8), out NoulHandle _)
            .Build();
        Assert.True(blank.IsSuccess);
        Assert.Equal(["JEV003", "JEV003"], WarningRules(blank.Value));

        // JEV005: a Score of 11 levels, and a Choice of 256 options.
        var many = JevQuestionSet.CreateBuilder()
            .Score("s", "How?", out KeyedScoreHandle _, levels =>
            {
                for (var i = 0; i < 11; i++)
                {
                    levels.Level("level");
                }
            })
            .Choice("c", "Which?", out KeyedChoiceHandle _, options =>
            {
                for (var i = 0; i < 256; i++)
                {
                    options.Option("option" + i);
                }
            })
            .Build();
        Assert.True(many.IsSuccess);
        Assert.Equal(["JEV005", "JEV005"], WarningRules(many.Value));
    }

    [Fact]
    public async Task Handles_ReadOnlyTheAnswersOfASetFromTheirOwnBuilder()
    {
        var builder = JevQuestionSet.CreateBuilder().Noul("first", "First?", out var first);
        var early = builder.Build().Value;
        builder.Noul("second", "Second?", out var second);
        var late = builder.Build().Value;
        var other = JevQuestionSet.CreateBuilder().Noul("first", "First?", out var foreign).Build().Value;

        var (http, jev, _) = CannedJev.Client("""
            { "model": "m", "answers": { "first": { "type": "noul", "noul": 0.2 }, "second": { "type": "noul", "noul": 0.7 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (jev)
        {
            var earlyAnswers = (await jev.EvaluateAsync(early, "x", CancellationToken.None)).Value;
            var lateAnswers = (await jev.EvaluateAsync(late, "x", CancellationToken.None)).Value;
            var otherAnswers = (await jev.EvaluateAsync(other, "x", CancellationToken.None)).Value;

            // A handle works with every set its builder built, if the question existed when the set was built.
            Assert.Equal(0.2, earlyAnswers.Get(first).Probability);
            Assert.Equal(0.2, lateAnswers.Get(first).Probability);
            Assert.Equal(0.7, lateAnswers.Get(second).Probability);

            // It does not work with a set built before its question was added, another builder's set, or when it is default.
            Assert.Throws<ArgumentException>(() => earlyAnswers.Get(second));
            Assert.Throws<ArgumentException>(() => otherAnswers.Get(first));
            Assert.Throws<ArgumentException>(() => earlyAnswers.Get(foreign));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(NoulHandle)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(KeyedChoiceHandle)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(KeyedScoreHandle)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(ChoiceHandle<ServiceTeam>)));
            Assert.Throws<ArgumentException>(() => lateAnswers.Get(default(ScoreHandle<Priority>)));
        }
    }

    [Fact]
    public async Task AMissingAnswer_FailsTheCallAsInvalidResponse()
    {
        var set = JevQuestionSet.CreateBuilder().Noul("a", "A?", out NoulHandle _).Noul("b", "B?", out NoulHandle _).Build().Value;
        var (http, jev, _) = CannedJev.Client("""
            { "model": "m", "answers": { "a": { "type": "noul", "noul": 0.2 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync(set, "x", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        }
    }

    [Fact]
    public void AConfigurator_WorksOnlyInsideItsCallback()
    {
        KeyedChoiceOptionsBuilder? stored = null;
        NoulCriteriaBuilder? storedNoul = null;
        JevQuestionSet.CreateBuilder()
            .Choice("c", "Which?", out KeyedChoiceHandle _, options =>
            {
                stored = options;
                options.Option("x");
            })
            .Noul("n", "A?", out NoulHandle _, criteria => storedNoul = criteria);

        Assert.Throws<InvalidOperationException>(() => stored!.Option("y"));
        Assert.Throws<InvalidOperationException>(() => storedNoul!.WhenTrue("yes"));
    }

    [Fact]
    public void Arguments_AreCheckedWhenAQuestionIsAdded()
    {
        var builder = JevQuestionSet.CreateBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.Noul(null!, "A?", out NoulHandle _));
        Assert.Throws<ArgumentException>(() => builder.Noul("a", default, out NoulHandle _));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Choice(
            "c", "Which?", out ChoiceHandle<ServiceTeam> _, options => options.Describe((ServiceTeam)99, "x")));
    }

    [Fact]
    public void EnumQuestions_KeyTheMembersInSnakeCase_AndSkipAliases()
    {
        var set = JevQuestionSet.CreateBuilder().Choice("route", "Where to?", out ChoiceHandle<Route> _).Build().Value;

        using var questions = JsonDocument.Parse(set.QuestionsUtf8.ToArray());
        var criteria = questions.RootElement.GetProperty("route").GetProperty("criteria");
        Assert.Equal(["send_to_billing", "needs_human"], criteria.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task EnumAttributes_AreNotReadByTheBuilder()
    {
        // Department carries [Criteria] on its members; the builder sends no description for them.
        var set = JevQuestionSet.CreateBuilder().Choice("d", "Which?", out ChoiceHandle<Department> department).Build().Value;

        using var questions = JsonDocument.Parse(set.QuestionsUtf8.ToArray());
        var criteria = questions.RootElement.GetProperty("d").GetProperty("criteria");
        Assert.Equal(JsonValueKind.Null, criteria.GetProperty("billing").ValueKind);

        var (http, jev, _) = CannedJev.Client("""
            { "model": "m", "answers": { "d": { "type": "choice", "choice": "sales", "probabilities": { "billing": 0.1, "technical": 0.2, "sales": 0.7 }, "confidence": 0.5 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync(set, "x", CancellationToken.None);

            Assert.Equal(Department.Sales, result.Value.Get(department).Value);
        }
    }

    [Fact]
    public async Task AnswersToUnknownKeys_AreIgnored()
    {
        var set = JevQuestionSet.CreateBuilder().Noul("a", "A?", out var a).Build().Value;
        var (http, jev, _) = CannedJev.Client("""
            { "model": "m", "answers": { "extra": { "type": "noul", "noul": 0.9 }, "a": { "type": "noul", "noul": 0.2 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
            """);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync(set, "x", CancellationToken.None);

            Assert.Equal(0.2, result.Value.Get(a).Probability);
        }
    }

    [Fact]
    public async Task AFakeThatImplementsTheTwoAbstractMembers_EvaluatesABuiltSet()
    {
        var set = JevQuestionSet.CreateBuilder().Noul("a", "A?", out var a).Build().Value;

        var result = await ((IJevClient)new FakeClient()).EvaluateAsync(set, "x", CancellationToken.None);

        Assert.Equal(0.4, result.Value.Get(a).Probability);
    }

    [Fact]
    public void ExamplesOnAJsonCriterion_Throw()
    {
        var json = JevCriterion.Json(JevContent.FromUtf8Json("{}"u8));

        Assert.Throws<InvalidOperationException>(() => json.WithExamples("x"));
        Assert.Throws<InvalidOperationException>(() => json.WithNotFor("x"));
        Assert.Throws<ArgumentException>(() => JevCriterion.Json(JevContent.FromString("text")));
    }

    private sealed class FakeClient : IJevClient
    {
        public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct)
            => ValueTask.FromResult(Result<SystemOneResponse, JevError>.Success(new SystemOneResponse
            {
                Model = "fake",
                Answers = new Dictionary<string, JevAnswer> { ["a"] = new NoulAnswer { Noul = 0.4 } },
                Usage = new JevUsage { InputTokens = 1, OutputTokens = 1 },
            }));

        public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private enum Empty
    {
    }

    [Fact]
    public void AnEnumWithNoMembers_FailsAsJev001AndJev002()
    {
        var choice = JevQuestionSet.CreateBuilder().Choice("c", "Which?", out ChoiceHandle<Empty> _).Build();
        var score = JevQuestionSet.CreateBuilder().Score("s", "How?", out ScoreHandle<Empty> _, levels => { }).Build();
        var keyedChoice = JevQuestionSet.CreateBuilder().Choice("c", "Which?", out KeyedChoiceHandle _, options => { }).Build();
        var enumScore = JevQuestionSet.CreateBuilder().Score("s", "How?", out ScoreHandle<Priority> _, levels => { }).Build();

        Assert.Equal(["JEV001"], Rules(choice.Error));
        Assert.Equal(["JEV002"], Rules(score.Error));
        Assert.Equal(["JEV001"], Rules(keyedChoice.Error));
        Assert.Equal(["JEV104", "JEV104", "JEV104"], Rules(enumScore.Error));
    }

    [Fact]
    public void ANoulAndAnEnumChoice_BuildWithoutAConfigurator()
    {
        Assert.True(JevQuestionSet.CreateBuilder().Noul("n", "A?", out NoulHandle _).Build().IsSuccess);
        Assert.True(JevQuestionSet.CreateBuilder().Choice("c", "Which?", out ChoiceHandle<ServiceTeam> _).Build().IsSuccess);
    }

    private static string[] Rules(JevError error) => [.. error.Failures.Select(f => f.Rule)];

    private static string[] WarningRules(JevQuestionSet set) => [.. set.Warnings.Select(f => f.Rule)];
}
