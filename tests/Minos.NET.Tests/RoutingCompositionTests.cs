namespace Minos.Tests;

/// <summary>Escalation and fallback in one pipeline: the documented order, and the order the other way round.</summary>
public sealed class RoutingCompositionTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private static readonly DecisionUsage ClefUsage = new() { InputTokens = 10, OutputTokens = 2, Cost = 0.001 };
    private static readonly DecisionUsage HostedUsage = new() { InputTokens = 20, OutputTokens = 3, Cost = 0.01 };

    // is_urgent is confident by the default threshold of 0.6; department and severity are unsure.
    private static readonly Dictionary<string, QuestionAnswer> UnsureAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.95),
        ["department"] = QuestionAnswer.Choice(0, 0.5, [0.5, 0.3, 0.2]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.4, [0.3, 0.4, 0.3]),
    };

    private static readonly Dictionary<string, QuestionAnswer> SureAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.95),
        ["department"] = QuestionAnswer.Choice(0, 0.9, [0.9, 0.05, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> HostedAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.1),
        ["department"] = QuestionAnswer.Choice(1, 0.85, [0.1, 0.85, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 2.0, 0.8, [0.05, 0.15, 0.8]),
    };

    private static void FastRetries(DecisionRetryOptions options)
    {
        options.InitialBackoff = TimeSpan.FromMilliseconds(1);
        options.Jitter = false;
    }

    private static Action<EscalationOptions> Hosted => o => o.TargetName = "hosted";

    [Fact]
    public async Task Escalation_over_fallback_tells_one_flat_story()
    {
        // The documented order: UseEscalation, then UseFallback, then UseRetries, outermost first.
        var local = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var clef = StubDecisionClient.Answering(UnsureAnswers, "clef-model", ClefUsage);
        var hosted = StubDecisionClient.Answering(HostedAnswers, "hosted-model", HostedUsage);
        using var client = new DecisionClientBuilder(local)
            .UseEscalation(hosted, Hosted)
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", clef)))
            .UseRetries(FastRetries)
            .Build();

        var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        // The local server's transient failure was retried inside step 0 before the chain moved on.
        Assert.Equal(3, local.Calls);
        Assert.Equal(1, clef.Calls);
        Assert.Equal(["department", "severity"], Assert.Single(hosted.Requests).Definition.Questions.Select(q => q.Key));
        Assert.Equal(
            [
                new DecisionCall("primary", null, null, IsCached: false) { Outcome = CallOutcome.Failed, ErrorKind = DecisionErrorKind.Server },
                new DecisionCall("clef", "clef-model", ClefUsage, IsCached: false),
                new DecisionCall("hosted", "hosted-model", HostedUsage, IsCached: false),
            ],
            response.Path!.Calls);
        Assert.Equal(
            [
                new QuestionRoute("is_urgent", "clef", RouteReason.Confident, 0.95, null),
                new QuestionRoute("department", "hosted", RouteReason.Escalated, 0.5, 0.85),
                new QuestionRoute("severity", "hosted", RouteReason.Escalated, 0.4, 0.8),
            ],
            response.Path.Routes);
        Assert.Equal(new DecisionUsage { InputTokens = 30, OutputTokens = 5, Cost = 0.011 }, response.Usage);
        Assert.Equal("clef-model", response.Model);
        Assert.Equal(1, response.Answers[1].ChosenIndex);
    }

    [Fact]
    public async Task Escalation_over_fallback_with_no_unsure_answer_keeps_the_inner_path()
    {
        var clef = StubDecisionClient.Answering(SureAnswers, "clef-model", ClefUsage);
        var hosted = StubDecisionClient.Answering(HostedAnswers);
        using var client = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Network))
            .UseEscalation(hosted, Hosted)
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", clef)))
            .Build();

        var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        Assert.Equal(0, hosted.Calls);
        Assert.Equal(
            [
                new DecisionCall("primary", null, null, IsCached: false) { Outcome = CallOutcome.Failed, ErrorKind = DecisionErrorKind.Network },
                new DecisionCall("clef", "clef-model", ClefUsage, IsCached: false),
            ],
            response.Path!.Calls);
        Assert.All(response.Path.Routes, route => Assert.Equal(new { AnsweredBy = "clef", Reason = RouteReason.Confident }, new { route.AnsweredBy, route.Reason }));
        Assert.Equal(ClefUsage, response.Usage);
    }

    [Fact]
    public async Task Escalation_over_fallback_with_a_failed_target_keeps_the_inner_answerer()
    {
        var clef = StubDecisionClient.Answering(UnsureAnswers, "clef-model", ClefUsage);
        using var client = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Network))
            .UseEscalation(StubDecisionClient.Failing(DecisionErrorKind.Overloaded), Hosted)
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", clef)))
            .Build();

        var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        Assert.Equal(["primary", "clef", "hosted"], response.Path!.Calls.Select(c => c.ClientName));
        Assert.Equal(
            [
                new QuestionRoute("is_urgent", "clef", RouteReason.Confident, 0.95, null),
                new QuestionRoute("department", "clef", RouteReason.TargetFailed, 0.5, null),
                new QuestionRoute("severity", "clef", RouteReason.TargetFailed, 0.4, null),
            ],
            response.Path.Routes);
        Assert.Equal(ClefUsage, response.Usage);
    }

    [Fact]
    public async Task Escalation_without_an_inner_path_is_unchanged()
    {
        // The fallback stage passes step 0's answer through without a path, so escalation names it PrimaryName, as in 10.1.
        var local = StubDecisionClient.Answering(UnsureAnswers, "local-2b", ClefUsage);
        var clef = StubDecisionClient.Answering(SureAnswers);
        var hosted = StubDecisionClient.Answering(HostedAnswers, "hosted-model", HostedUsage);
        using var client = new DecisionClientBuilder(local)
            .UseEscalation(hosted, Hosted)
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", clef)))
            .UseRetries(FastRetries)
            .Build();

        var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        Assert.Equal(0, clef.Calls);
        Assert.Equal(
            [
                new DecisionCall("primary", "local-2b", ClefUsage, IsCached: false),
                new DecisionCall("hosted", "hosted-model", HostedUsage, IsCached: false),
            ],
            response.Path!.Calls);
        Assert.Equal(
            [
                new QuestionRoute("is_urgent", "primary", RouteReason.Confident, 0.95, null),
                new QuestionRoute("department", "hosted", RouteReason.Escalated, 0.5, 0.85),
                new QuestionRoute("severity", "hosted", RouteReason.Escalated, 0.4, 0.8),
            ],
            response.Path.Routes);
        Assert.Equal("local-2b", response.Model);
    }

    [Fact]
    public async Task Fallback_over_escalation_only_reacts_to_the_primary_failing()
    {
        // Fallback outside escalation: a failed target is escalation's own matter, and the chain does not move on.
        var clef = StubDecisionClient.Answering(SureAnswers, "clef-model");
        using (var client = new DecisionClientBuilder(StubDecisionClient.Answering(UnsureAnswers))
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", clef)))
            .UseEscalation(StubDecisionClient.Failing(DecisionErrorKind.Server), Hosted)
            .Build())
        {
            var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

            Assert.Equal(0, clef.Calls);
            Assert.Equal(["primary", "hosted"], response.Path!.Calls.Select(c => c.ClientName));
            Assert.Equal(RouteReason.TargetFailed, response.Path.Routes[1].Reason);
        }

        // The primary failing fails the escalation pipeline, and the chain moves on without escalating.
        var hosted = StubDecisionClient.Answering(HostedAnswers);
        using (var client = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", clef)))
            .UseEscalation(hosted, Hosted)
            .Build())
        {
            var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

            Assert.Equal(0, hosted.Calls);
            Assert.Equal(1, clef.Calls);
            Assert.Equal(
                [
                    new DecisionCall("primary", null, null, IsCached: false) { Outcome = CallOutcome.Failed, ErrorKind = DecisionErrorKind.Server },
                    new DecisionCall("clef", "clef-model", null, IsCached: false),
                ],
                response.Path!.Calls);
            Assert.All(response.Path.Routes, route => Assert.Equal(new { AnsweredBy = "clef", Reason = RouteReason.FellBack }, new { route.AnsweredBy, route.Reason }));
        }
    }
}
