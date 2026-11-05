using System.Globalization;
using Fake = Minos.Tests.EscalationTests.Fake;

namespace Minos.Tests;

public sealed class EscalationMergeTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private const double Sure = 0.95;
    private const double Unsure = 0.3;

    // Five questions of mixed kinds and option counts, so every question's offset differs between the full definition
    // and most subsets: offsets 0, 3, 3, 6 and 10.
    private static QuestionSetDefinition Mixed()
        => new(
            QuestionDefinition.Choice("department", DecisionContent.FromString("Which team?"), Option("billing"), Option("tech"), Option("other")),
            QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Is this urgent?")),
            QuestionDefinition.Score("severity", DecisionContent.FromString("How severe?"), "Low", "Medium", "High"),
            QuestionDefinition.Choice("channel", DecisionContent.FromString("Which channel?"), Option("mail"), Option("chat"), Option("phone"), Option("post")),
            QuestionDefinition.Score("effort", DecisionContent.FromString("How much effort?"), "1", "2", "3", "4", "5"));

    private static OptionDefinition Option(string key) => new(key, null);

    // The primary's answers: the questions whose bit is set in the mask are unsure.
    private static Dictionary<string, QuestionAnswer> PrimaryAnswers(int unsureMask)
    {
        double C(int bit) => (unsureMask & (1 << bit)) != 0 ? Unsure : Sure;
        return new(StringComparer.Ordinal)
        {
            ["department"] = QuestionAnswer.Choice(0, C(0), [C(0), 1 - C(0) - 0.01, 0.01]),
            ["is_urgent"] = QuestionAnswer.Noul((unsureMask & 2) != 0 ? 0.55 : 0.98),
            ["severity"] = QuestionAnswer.Score(2, 1.9, C(2), [0.01, 0.99 - C(2), C(2)]),
            ["channel"] = QuestionAnswer.Choice(3, C(3), [0.01, 0.01, 0.98 - C(3), C(3)]),
            ["effort"] = QuestionAnswer.Score(0, 0.2, C(4), [C(4), 0.97 - C(4), 0.01, 0.01, 0.01]),
        };
    }

    // The target's answers differ from the primary's in every field, with another probability source.
    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["department"] = QuestionAnswer.Choice(2, 0.7, [0.1, 0.2, 0.7], ProbabilitySource.Logprobs),
        ["is_urgent"] = QuestionAnswer.Noul(0.12, ProbabilitySource.Verbalized),
        ["severity"] = QuestionAnswer.Score(0, 0.4, 0.65, [0.65, 0.3, 0.05], ProbabilitySource.Logprobs),
        ["channel"] = QuestionAnswer.Choice(1, 0.8, [0.05, 0.8, 0.1, 0.05], ProbabilitySource.Verbalized),
        ["effort"] = QuestionAnswer.Score(4, 3.9, 0.6, [0.02, 0.03, 0.05, 0.3, 0.6], ProbabilitySource.Logprobs),
    };

    private static EscalationOptions Options() => new() { TargetName = "hosted" };

    private static void AssertSameAnswer(QuestionAnswer expected, QuestionAnswer actual)
    {
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.ChosenIndex, actual.ChosenIndex);
        Assert.Equal(expected.Probability, actual.Probability);
        Assert.Equal(expected.Expected, actual.Expected);
        Assert.Equal(expected.Confidence, actual.Confidence);
        Assert.Equal(expected.Probabilities.ToArray(), actual.Probabilities.ToArray());
        Assert.Equal(expected.ProbabilitySource, actual.ProbabilitySource);
    }

    [Fact]
    public async Task Merged_answers_equal_each_clients_full_answers_question_by_question()
    {
        var definition = Mixed();
        var full = Fake.Respond(definition, TargetAnswers);

        // Every pattern of unsure questions, from none to all five.
        for (var mask = 0; mask < 32; mask++)
        {
            var primaryAnswers = PrimaryAnswers(mask);
            using var client = new EscalatingDecisionClient(Fake.Answering(primaryAnswers), Fake.Answering(TargetAnswers), Options());
            var primaryFull = Fake.Respond(definition, primaryAnswers);

            var merged = (await client.EvaluateAsync(new DecisionRequest(definition, State))).Value;

            Assert.Same(definition, merged.Definition);
            for (var i = 0; i < 5; i++)
            {
                var escalated = (mask & (1 << i)) != 0;
                var route = merged.Path!.Routes[i];
                Assert.Equal(escalated ? "hosted" : "primary", route.AnsweredBy);
                Assert.Equal(escalated ? RouteReason.Escalated : RouteReason.Confident, route.Reason);
                AssertSameAnswer(escalated ? full.Answers[i] : primaryFull.Answers[i], merged.Answers[i]);
            }

            Assert.Equal(definition.ProbabilityCount, merged.Probabilities.Length);
        }
    }

    [Fact]
    public async Task The_merge_leaves_the_primarys_response_unchanged()
    {
        var definition = Mixed();
        DecisionResponse? primaryResponse = null;
        var primary = new Fake(request => primaryResponse = Fake.Respond(request.Definition, PrimaryAnswers(0b11111)));
        using var client = new EscalatingDecisionClient(primary, Fake.Answering(TargetAnswers), Options());

        var merged = (await client.EvaluateAsync(new DecisionRequest(definition, State))).Value;

        Assert.Equal(TargetAnswers["department"].Probabilities.ToArray(), merged.Answers[0].Probabilities.ToArray());
        var untouched = Fake.Respond(definition, PrimaryAnswers(0b11111));
        for (var i = 0; i < 5; i++)
        {
            AssertSameAnswer(untouched.Answers[i], primaryResponse!.Answers[i]);
        }
    }

    public static TheoryData<int?, int?, double?, int?, int?, double?, int?, int?, double?> UsageCases => new()
    {
        { 10, 2, null, 5, 1, 0.01, 15, 3, 0.01 },
        { null, null, null, null, null, null, null, null, null },
        { 10, null, null, null, null, null, 10, null, null },
        { null, null, null, null, 4, null, null, 4, null },
        { null, null, 0.5, null, null, 0.25, null, null, 0.75 },
    };

    [Theory]
    [MemberData(nameof(UsageCases))]
    public async Task Usage_sums_per_field_and_stays_null_only_when_no_call_reported_it(
        int? primaryInput, int? primaryOutput, double? primaryCost,
        int? targetInput, int? targetOutput, double? targetCost,
        int? input, int? output, double? cost)
    {
        var definition = Mixed();
        var primary = new Fake(request => Fake.Respond(
            request.Definition, PrimaryAnswers(0b00001), usage: new DecisionUsage { InputTokens = primaryInput, OutputTokens = primaryOutput, Cost = primaryCost }));
        var target = new Fake(request => Fake.Respond(
            request.Definition, TargetAnswers, usage: new DecisionUsage { InputTokens = targetInput, OutputTokens = targetOutput, Cost = targetCost }));
        using var client = new EscalatingDecisionClient(primary, target, Options());

        var merged = (await client.EvaluateAsync(new DecisionRequest(definition, State))).Value;

        if (input is null && output is null && cost is null)
        {
            Assert.Null(merged.Usage);
        }
        else
        {
            Assert.Equal(new DecisionUsage { InputTokens = input, OutputTokens = output, Cost = cost }, merged.Usage);
        }
    }

    [Fact]
    public async Task Calls_list_primary_then_target_with_each_model_usage_and_IsCached()
    {
        var primaryUsage = new DecisionUsage { InputTokens = 10, OutputTokens = 2 };
        var targetUsage = new DecisionUsage { InputTokens = 5, OutputTokens = 1, Cost = 0.01 };
        var primary = new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => PrimaryAnswers(0b01000)[q.Key])], "local-2b", primaryUsage) { IsCached = true });
        var target = new Fake(request => Fake.Respond(request.Definition, TargetAnswers, "hosted-model", targetUsage));
        using var client = new EscalatingDecisionClient(primary, target, Options());

        var merged = (await client.EvaluateAsync(new DecisionRequest(Mixed(), State))).Value;

        Assert.Equal(
            [new DecisionCall("primary", "local-2b", primaryUsage, IsCached: true), new DecisionCall("hosted", "hosted-model", targetUsage, IsCached: false)],
            merged.Path!.Calls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task IsCached_is_true_only_when_every_call_was_cached(bool primaryCached, bool targetCached, bool expected)
    {
        var primary = new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => PrimaryAnswers(0b00100)[q.Key])]) { IsCached = primaryCached });
        var target = new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => TargetAnswers[q.Key])]) { IsCached = targetCached });
        using var client = new EscalatingDecisionClient(primary, target, Options());

        var merged = (await client.EvaluateAsync(new DecisionRequest(Mixed(), State))).Value;

        Assert.Equal(expected, merged.IsCached);
        Assert.Equal([primaryCached, targetCached], merged.Path!.Calls.Select(c => c.IsCached));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_an_escalated_answer_IsCached_is_the_primarys(bool primaryCached)
    {
        // All confident: one call. Target failed: every answer is still the primary's.
        var primarySure = new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => PrimaryAnswers(0)[q.Key])]) { IsCached = primaryCached });
        var primaryUnsure = new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => PrimaryAnswers(0b00001)[q.Key])]) { IsCached = primaryCached });
        using var sure = new EscalatingDecisionClient(primarySure, new Fake(_ => null), Options());
        using var failed = new EscalatingDecisionClient(primaryUnsure, new Fake(_ => null), Options());

        Assert.Equal(primaryCached, (await sure.EvaluateAsync(new DecisionRequest(Mixed(), State))).Value.IsCached);
        Assert.Equal(primaryCached, (await failed.EvaluateAsync(new DecisionRequest(Mixed(), State))).Value.IsCached);
    }

    [Fact]
    public async Task Model_Id_and_UpstreamProvider_are_the_primarys()
    {
        var primary = new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => PrimaryAnswers(0b10010)[q.Key])], "local-2b")
        {
            Id = "primary-id",
            UpstreamProvider = "primary-host",
        });
        var target = new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => TargetAnswers[q.Key])], "hosted-model")
        {
            Id = "target-id",
            UpstreamProvider = "target-host",
        });
        using var client = new EscalatingDecisionClient(primary, target, Options());

        var merged = (await client.EvaluateAsync(new DecisionRequest(Mixed(), State))).Value;
        using var sureClient = new EscalatingDecisionClient(
            new Fake(request => new DecisionResponse(request.Definition, [.. request.Definition.Questions.Select(q => PrimaryAnswers(0)[q.Key])], "local-2b") { Id = "primary-id", UpstreamProvider = "primary-host" }),
            target,
            Options());
        var sure = (await sureClient.EvaluateAsync(new DecisionRequest(Mixed(), State))).Value;

        foreach (var response in new[] { merged, sure })
        {
            Assert.Equal("local-2b", response.Model);
            Assert.Equal("primary-id", response.Id);
            Assert.Equal("primary-host", response.UpstreamProvider);
        }
    }

    [Fact]
    public async Task The_subset_definition_is_reused_for_the_same_pattern()
    {
        var definition = Mixed();
        var seen = new List<QuestionSetDefinition>();
        var primary = new Fake(request => Fake.Respond(
            request.Definition, PrimaryAnswers(string.Equals(request.State.ToString(), "a", StringComparison.Ordinal) ? 0b00101 : 0b01010)));
        using var client = new EscalatingDecisionClient(primary, Fake.Answering(TargetAnswers, r => seen.Add(r.Definition)), Options());

        await client.EvaluateAsync(new DecisionRequest(definition, "a"));
        await client.EvaluateAsync(new DecisionRequest(definition, "a"));
        await client.EvaluateAsync(new DecisionRequest(definition, "b"));
        await client.EvaluateAsync(new DecisionRequest(definition, "b"));

        Assert.Same(seen[0], seen[1]);
        Assert.Same(seen[2], seen[3]);
        Assert.NotSame(seen[0], seen[2]);
        Assert.Equal(["department", "severity"], seen[0].Questions.Select(q => q.Key));
        Assert.Equal(["is_urgent", "channel"], seen[2].Questions.Select(q => q.Key));
        Assert.Same(definition.Questions[0], seen[0].Questions[0]);
        Assert.Equal(2, EscalationMerge.CachedSubsetCount(definition));
    }

    // Questions named q0, q1, ... with a Noul each: the state is the unsure mask, as a number.
    private static QuestionSetDefinition Nouls(int count)
        => new([.. Enumerable.Range(0, count).Select(i => QuestionDefinition.Noul($"q{i}", DecisionContent.FromString("Is it?")))]);

    private static Fake NoulPrimary()
        => new(request =>
        {
            var mask = ulong.Parse(request.State.ToString(), CultureInfo.InvariantCulture);
            var questions = request.Definition.Questions;
            return new DecisionResponse(request.Definition, [.. questions.Select((_, i) => QuestionAnswer.Noul(i < 64 && (mask & (1UL << i)) != 0 ? 0.5 : 0.99))]);
        });

    // The target answers each question with 0.01 times its index plus 0.001.
    private static Fake NoulTarget(Action<DecisionRequest>? seen = null)
        => new(
            request => new DecisionResponse(
                request.Definition,
                [.. request.Definition.Questions.Select(q => QuestionAnswer.Noul((int.Parse(q.Key.AsSpan(1), CultureInfo.InvariantCulture) * 0.01) + 0.001))]),
            seen);

    [Fact]
    public async Task The_subset_cache_holds_at_most_64_patterns()
    {
        var definition = Nouls(8);
        using var client = new EscalatingDecisionClient(NoulPrimary(), NoulTarget(), Options());

        for (var mask = 1; mask <= 100; mask++)
        {
            var response = (await client.EvaluateAsync(new DecisionRequest(definition, mask.ToString(CultureInfo.InvariantCulture)))).Value;

            for (var i = 0; i < 8; i++)
            {
                var escalated = (mask & (1 << i)) != 0;
                Assert.Equal(escalated ? (i * 0.01) + 0.001 : 0.99, response.Answers[i].Probability);
                Assert.Equal(escalated ? RouteReason.Escalated : RouteReason.Confident, response.Path!.Routes[i].Reason);
            }
        }

        Assert.Equal(64, EscalationMerge.CachedSubsetCount(definition));
    }

    [Fact]
    public async Task A_set_of_more_than_64_questions_builds_its_subset_each_time()
    {
        var definition = Nouls(65);
        var seen = new List<QuestionSetDefinition>();
        using var client = new EscalatingDecisionClient(NoulPrimary(), NoulTarget(r => seen.Add(r.Definition)), Options());

        // q0 and q63 unsure: the last question, q64, is past what the mask can hold and stays confident.
        var state = ((1UL << 63) | 1UL).ToString(CultureInfo.InvariantCulture);
        var first = (await client.EvaluateAsync(new DecisionRequest(definition, state))).Value;
        await client.EvaluateAsync(new DecisionRequest(definition, state));

        Assert.NotSame(seen[0], seen[1]);
        Assert.Equal(["q0", "q63"], seen[0].Questions.Select(q => q.Key));
        Assert.Equal(0.001, first.Answers[0].Probability);
        Assert.Equal(0.631, first.Answers[63].Probability, 12);
        Assert.Equal(0.99, first.Answers[64].Probability);
        Assert.Equal(0, EscalationMerge.CachedSubsetCount(definition));
    }

    [Fact]
    public async Task A_set_of_more_than_64_questions_escalates_a_question_past_the_64th()
    {
        var definition = Nouls(70);
        var seen = new List<QuestionSetDefinition>();
        var primary = new Fake(request => new DecisionResponse(
            request.Definition, [.. request.Definition.Questions.Select((_, i) => QuestionAnswer.Noul(i is 2 or 68 ? 0.5 : 0.99))]));
        using var client = new EscalatingDecisionClient(primary, NoulTarget(r => seen.Add(r.Definition)), Options());

        var response = (await client.EvaluateAsync(new DecisionRequest(definition, State))).Value;

        Assert.Equal(["q2", "q68"], Assert.Single(seen).Questions.Select(q => q.Key));
        Assert.Equal(0.681, response.Answers[68].Probability, 12);
        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[68].Reason);
    }

    [Fact]
    public async Task Concurrent_calls_never_mix_answers()
    {
        var definition = Mixed();

        // Call n: the unsure mask is one of four patterns, by n, and each client's answers carry n.
        static int MaskOf(int n) => (n % 4) switch { 0 => 0b00001, 1 => 0b10110, 2 => 0b01000, _ => 0b11111 };
        static double Mark(int n) => n / 1000.0;

        var primary = new Fake(
            request =>
            {
                var n = int.Parse(request.State.ToString(), CultureInfo.InvariantCulture);
                var answers = PrimaryAnswers(MaskOf(n));
                answers["is_urgent"] = QuestionAnswer.Noul(((MaskOf(n) & 2) != 0 ? 0.45 : 0.9) + Mark(n));
                return Fake.Respond(request.Definition, answers, model: $"local-{n}");
            },
            yield: true);
        var target = new Fake(
            request =>
            {
                var n = int.Parse(request.State.ToString(), CultureInfo.InvariantCulture);
                var answers = new Dictionary<string, QuestionAnswer>(TargetAnswers, StringComparer.Ordinal)
                {
                    ["is_urgent"] = QuestionAnswer.Noul(0.1 + Mark(n)),
                    ["department"] = QuestionAnswer.Choice(2, 0.5 + Mark(n), [0.5 - Mark(n), 0, 0.5 + Mark(n)]),
                    ["effort"] = QuestionAnswer.Score(4, 3.9, 0.5 + Mark(n), [0.5 - Mark(n), 0, 0, 0, 0.5 + Mark(n)]),
                };
                return Fake.Respond(request.Definition, answers, model: $"hosted-{n}");
            },
            yield: true);
        using var client = new EscalatingDecisionClient(primary, target, Options());

        var calls = Enumerable.Range(0, 32)
            .Select(n => Task.Run(async () => (n, (await client.EvaluateAsync(new DecisionRequest(definition, n.ToString(CultureInfo.InvariantCulture)))).Value)))
            .ToArray();
        var results = await Task.WhenAll(calls);

        foreach (var (n, response) in results)
        {
            var mask = MaskOf(n);
            Assert.Equal($"local-{n}", response.Model);
            Assert.Equal($"hosted-{n}", response.Path!.Calls[1].Model);

            var urgent = response.Answers[1];
            Assert.Equal((mask & 2) != 0 ? 0.1 + Mark(n) : 0.9 + Mark(n), urgent.Probability, 12);

            var department = response.Answers[0];
            if ((mask & 1) != 0)
            {
                Assert.Equal([0.5 - Mark(n), 0, 0.5 + Mark(n)], department.Probabilities.ToArray());
            }
            else
            {
                Assert.Equal(PrimaryAnswers(mask)["department"].Probabilities.ToArray(), department.Probabilities.ToArray());
            }

            var effort = response.Answers[4];
            if ((mask & 16) != 0)
            {
                Assert.Equal([0.5 - Mark(n), 0, 0, 0, 0.5 + Mark(n)], effort.Probabilities.ToArray());
            }
            else
            {
                Assert.Equal(PrimaryAnswers(mask)["effort"].Probabilities.ToArray(), effort.Probabilities.ToArray());
            }

            for (var i = 0; i < 5; i++)
            {
                Assert.Equal((mask & (1 << i)) != 0 ? "hosted" : "primary", response.Path.Routes[i].AnsweredBy);
            }
        }

        Assert.Equal(4, EscalationMerge.CachedSubsetCount(definition));
    }
}
