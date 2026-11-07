using Microsoft.Extensions.AI;
using MinosClient = Minos.IDecisionClient;
using MinosRequest = Minos.DecisionRequest;
using MinosResponse = Minos.DecisionResponse;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// The Minos routing stages, <c>UseFallback</c> and <c>UseEscalation</c>, built over <see cref="UpstreamDecisionClient"/>
/// instances that wrap upstream fakes, against the same stack over Minos fakes that behave the same way: the path, the
/// answered-by names and the merged answers are the same. Each scenario is also checked on its own terms, so the two
/// stacks cannot agree on a wrong result.
/// </summary>
/// <remarks>
/// An upstream answer carries no confidence, so the Minos fakes give the confidence <see cref="UpstreamDecisionClient"/>
/// assumes for it, the probability of the chosen option, as <see cref="ScriptedAnswer"/> describes. A transient upstream
/// <see cref="DecisionClientException"/> is the failure that <see cref="UpstreamDecisionClient"/> reports as
/// <see cref="DecisionErrorKind.Server"/>, so the Minos fake fails with that kind.
/// </remarks>
public sealed class PipelineTests
{
    private const string Model = "model-x";

    private static readonly QuestionSetDefinition Definition = new(
        QuestionDefinition.Noul("is_urgent", "Is it urgent?"),
        QuestionDefinition.Choice("team", "Which team?", new OptionDefinition("billing", "Payments"), new OptionDefinition("tech", "Bugs")),
        QuestionDefinition.Score("severity", "How severe?", "Low", "Medium", "High"));

    private static readonly string[] AllKeys = ["is_urgent", "team", "severity"];

    // Sure of every question: a Noul far from 0.5, and a Choice and a Score whose chosen probability is at least 0.6.
    private static readonly Dictionary<string, ScriptedAnswer> Sure = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = ScriptedAnswer.Noul(0.9),
        ["team"] = ScriptedAnswer.Choice(0, 0.8, 0.2),
        ["severity"] = ScriptedAnswer.Score(1.6, 0.1, 0.2, 0.7),
    };

    // Unsure of the Choice only: its chosen probability, 0.55, is below the default threshold of 0.6.
    private static readonly Dictionary<string, ScriptedAnswer> UnsureOfTeam = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = ScriptedAnswer.Noul(0.05),
        ["team"] = ScriptedAnswer.Choice(0, 0.55, 0.45),
        ["severity"] = ScriptedAnswer.Score(0.3, 0.75, 0.2, 0.05),
    };

    private static readonly Dictionary<string, ScriptedAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["team"] = ScriptedAnswer.Choice(1, 0.1, 0.9),
    };

    [Fact]
    public async Task UseFallback_AfterATransientFailure_MatchesTheSameStackOverMinosFakes()
    {
        var upstream = await RunAsync(new UpstreamSide(), Fallback, Scenario.Fallback);
        var minos = await RunAsync(new MinosSide(), Fallback, Scenario.Fallback);

        AssertSame(minos, upstream);
        var path = upstream.Response.Path!;
        Assert.Equal(
            [("primary", CallStatus.Failed, (DecisionErrorKind?)DecisionErrorKind.Server), ("backup", CallStatus.Answered, null)],
            path.Calls.Select(c => (c.ClientName, c.Status, c.ErrorKind)));
        Assert.All(path.Routes, route => Assert.Equal(("backup", RouteReason.FellBack), (route.AnsweredBy, route.Reason)));
        Assert.Equal(AllKeys, path.Routes.Select(r => r.Key));
        Assert.Equal(AllKeys, Assert.Single(upstream.Calls["primary"]));
        Assert.Equal(AllKeys, Assert.Single(upstream.Calls["backup"]));
    }

    [Fact]
    public async Task UseEscalation_ForALowConfidenceAnswer_MatchesTheSameStackOverMinosFakes()
    {
        var upstream = await RunAsync(new UpstreamSide(), Escalation, Scenario.Escalation);
        var minos = await RunAsync(new MinosSide(), Escalation, Scenario.Escalation);

        AssertSame(minos, upstream);
        var path = upstream.Response.Path!;
        Assert.Equal(
            [("is_urgent", "primary", RouteReason.Confident), ("team", "target", RouteReason.Escalated), ("severity", "primary", RouteReason.Confident)],
            path.Routes.Select(r => (r.Key, r.AnsweredBy, r.Reason)));
        Assert.Equal(0.55, path.Routes[1].PrimaryCertainty, 10);
        Assert.Equal(0.9, path.Routes[1].TargetCertainty!.Value, 10);
        Assert.Equal(["primary", "target"], path.Calls.Select(c => c.ClientName));
        Assert.Equal(["team"], Assert.Single(upstream.Calls["target"]));

        // The merged answers: the target's for the escalated Choice, the primary's for the rest.
        Assert.Equal(1, upstream.Response.Answers[1].ChosenIndex);
        Assert.Equal(0.05, upstream.Response.Answers[0].Probability);
        Assert.Equal(0.3, upstream.Response.Answers[2].Expected);
    }

    [Fact]
    public async Task UseEscalation_OverUseFallback_MatchesTheSameStackOverMinosFakes()
    {
        var upstream = await RunAsync(new UpstreamSide(), EscalationOverFallback, Scenario.EscalationOverFallback);
        var minos = await RunAsync(new MinosSide(), EscalationOverFallback, Scenario.EscalationOverFallback);

        AssertSame(minos, upstream);
        var path = upstream.Response.Path!;
        Assert.Equal(
            [("primary", CallStatus.Failed), ("backup", CallStatus.Answered), ("target", CallStatus.Answered)],
            path.Calls.Select(c => (c.ClientName, c.Status)));
        Assert.Equal(
            [("is_urgent", "backup"), ("team", "target"), ("severity", "backup")],
            path.Routes.Select(r => (r.Key, r.AnsweredBy)));
        // docs/pipeline.md: when escalation wraps fallback, a question the outer stage finds confident keeps the inner
        // route, except that a FellBack route reads Confident. The backup's routes therefore read Confident here.
        Assert.Equal(
            [RouteReason.Confident, RouteReason.Escalated, RouteReason.Confident],
            path.Routes.Select(r => r.Reason));
        Assert.Equal(["team"], Assert.Single(upstream.Calls["target"]));
    }

    private enum Scenario
    {
        Fallback,
        Escalation,
        EscalationOverFallback,
    }

    private static MinosClient Fallback(Func<string, MinosClient> client)
        => client("primary").AsBuilder()
            .UseFallback(options => options.Steps.Add(new FallbackStep("backup", client("backup"))))
            .Build();

    private static MinosClient Escalation(Func<string, MinosClient> client)
        => client("primary").AsBuilder()
            .UseEscalation(client("target"), options => options.TargetName = "target")
            .Build();

    private static MinosClient EscalationOverFallback(Func<string, MinosClient> client)
        => client("primary").AsBuilder()
            .UseEscalation(client("target"), options => options.TargetName = "target")
            .UseFallback(options => options.Steps.Add(new FallbackStep("backup", client("backup"))))
            .Build();

    // Who answers what in each scenario; null is a transient failure.
    private static Dictionary<string, ScriptedAnswer>? Behaviour(Scenario scenario, string name) => (scenario, name) switch
    {
        (Scenario.Fallback, "primary") => null,
        (Scenario.Fallback, _) => Sure,
        (Scenario.Escalation, "primary") => UnsureOfTeam,
        (Scenario.EscalationOverFallback, "primary") => null,
        (Scenario.EscalationOverFallback, "backup") => UnsureOfTeam,
        (_, "target") => TargetAnswers,
        _ => throw new InvalidOperationException("No behaviour for " + name + "."),
    };

    private static async Task<Run> RunAsync(Side side, Func<Func<string, MinosClient>, MinosClient> build, Scenario scenario)
    {
        var calls = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        using var client = build(name =>
        {
            var (minosClient, record) = side.Create(Behaviour(scenario, name));
            calls[name] = record;
            return minosClient;
        });

        var result = await client.EvaluateAsync(new MinosRequest(Definition, "ticket text") { Model = Model });
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return new Run(result.Value, calls);
    }

    private static void AssertSame(Run expected, Run actual)
    {
        Assert.NotNull(actual.Response.Path);
        Assert.Equal(expected.Response.Path!.Routes, actual.Response.Path!.Routes);
        Assert.Equal(expected.Response.Path.Calls, actual.Response.Path.Calls);
        AnswerAssert.Same(expected.Response.Answers, actual.Response.Answers);
        Assert.Equal(expected.Response.Model, actual.Response.Model);
        Assert.Equal(expected.Response.Usage, actual.Response.Usage);
        Assert.Equal(expected.Calls.Keys.Order(), actual.Calls.Keys.Order());
        foreach (var (name, record) in expected.Calls)
        {
            Assert.Equal(record, actual.Calls[name]);
        }
    }

    private sealed record Run(MinosResponse Response, Dictionary<string, List<string[]>> Calls);

    private abstract class Side
    {
        public abstract (MinosClient Client, List<string[]> Calls) Create(Dictionary<string, ScriptedAnswer>? answers);
    }

    private sealed class UpstreamSide : Side
    {
        public override (MinosClient, List<string[]>) Create(Dictionary<string, ScriptedAnswer>? answers)
        {
            var fake = new ScriptedUpstreamClient("fake-upstream", Model, new UsageDetails { InputTokenCount = 10, OutputTokenCount = 4 });
            if (answers is null)
            {
                fake.Failure = new DecisionClientException("The provider is overloaded.", isTransient: true);
            }
            else
            {
                Fill(fake.Script, answers);
            }

            return (fake.AsMinosDecisionClient(), fake.Calls);
        }
    }

    private sealed class MinosSide : Side
    {
        public override (MinosClient, List<string[]>) Create(Dictionary<string, ScriptedAnswer>? answers)
        {
            var fake = new ScriptedMinosClient("fake-minos", Model, new DecisionUsage { InputTokens = 10, OutputTokens = 4 });
            if (answers is null)
            {
                fake.Error = new DecisionError(DecisionErrorKind.Server, "The provider is overloaded.");
            }
            else
            {
                Fill(fake.Script, answers);
            }

            return (fake, fake.Calls);
        }
    }

    private static void Fill(Dictionary<string, ScriptedAnswer> script, Dictionary<string, ScriptedAnswer> answers)
    {
        foreach (var (key, answer) in answers)
        {
            script[key] = answer;
        }
    }
}
