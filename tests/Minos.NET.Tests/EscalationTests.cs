using System.Diagnostics;
using ZeroAlloc.Results;

namespace Minos.Tests;

public sealed class EscalationTests
{
    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    // Primary answers that meet the default threshold of 0.6: certainties 0.97, 0.9 and 0.9.
    private static readonly Dictionary<string, QuestionAnswer> SurePrimary = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.9, [0.9, 0.05, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.5, [0.3, 0.5, 0.2]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.8, [0.1, 0.8, 0.1]),
    };

    private static EscalationOptions Options(Action<EscalationOptions>? configure = null)
    {
        var options = new EscalationOptions { TargetName = "hosted" };
        configure?.Invoke(options);
        return options;
    }

    private static Dictionary<string, QuestionAnswer> Primary(string key, QuestionAnswer answer)
        => new(SurePrimary, StringComparer.Ordinal) { [key] = answer };

    private static async Task<(DecisionResponse Response, List<DecisionRequest> TargetRequests)> RunAsync(
        Dictionary<string, QuestionAnswer> primary, EscalationOptions? options = null, DecisionRequest? request = null)
    {
        var targetRequests = new List<DecisionRequest>();
        using var client = new EscalatingDecisionClient(
            Fake.Answering(primary),
            Fake.Answering(TargetAnswers, targetRequests.Add),
            options ?? Options());
        var result = await client.EvaluateAsync(request ?? new DecisionRequest(Triage, State));
        Assert.True(result.IsSuccess);
        return (result.Value, targetRequests);
    }

    private static string[] Keys(QuestionSetDefinition definition) => [.. definition.Questions.Select(q => q.Key)];

    [Fact]
    public async Task A_Choice_below_its_threshold_is_re_asked()
    {
        var (response, sent) = await RunAsync(Primary("department", QuestionAnswer.Choice(0, 0.59, [0.59, 0.4, 0.01])));

        Assert.Equal(["department"], Keys(Assert.Single(sent).Definition));
        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[1].Reason);
    }

    [Fact]
    public async Task A_Score_below_its_threshold_is_re_asked()
    {
        var (response, sent) = await RunAsync(Primary("severity", QuestionAnswer.Score(1, 1.0, 0.5, [0.25, 0.5, 0.25])));

        Assert.Equal(["severity"], Keys(Assert.Single(sent).Definition));
        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[2].Reason);
    }

    [Fact]
    public async Task A_Noul_near_one_half_is_re_asked()
    {
        var (response, sent) = await RunAsync(Primary("is_urgent", QuestionAnswer.Noul(0.45)));

        Assert.Equal(["is_urgent"], Keys(Assert.Single(sent).Definition));
        Assert.Equal(new QuestionRoute("is_urgent", "hosted", RouteReason.Escalated, 0.55, 0.8), response.Path!.Routes[0]);
    }

    [Fact]
    public async Task A_Noul_at_0_05_is_not_re_asked()
    {
        // Certainty is max(p, 1 - p) = 0.95: a confident "no", not an unsure answer.
        var (response, sent) = await RunAsync(Primary("is_urgent", QuestionAnswer.Noul(0.05)), Options(o => o.DefaultThreshold = 0.9));

        Assert.Empty(sent);
        Assert.Equal(new QuestionRoute("is_urgent", "primary", RouteReason.Confident, 0.95, null), response.Path!.Routes[0]);
    }

    [Fact]
    public async Task A_confident_answer_is_never_re_asked()
    {
        var primary = new Dictionary<string, QuestionAnswer>(StringComparer.Ordinal)
        {
            ["is_urgent"] = QuestionAnswer.Noul(0.5),
            ["department"] = QuestionAnswer.Choice(0, 0.95, [0.95, 0.04, 0.01]),
            ["severity"] = QuestionAnswer.Score(1, 1.0, 0.4, [0.3, 0.4, 0.3]),
        };

        var (response, sent) = await RunAsync(primary);

        Assert.Equal(["is_urgent", "severity"], Keys(Assert.Single(sent).Definition));
        Assert.Equal(new QuestionRoute("department", "primary", RouteReason.Confident, 0.95, null), response.Path!.Routes[1]);
        Assert.Equal(0, response.Answers[1].ChosenIndex);
        Assert.Equal([0.95, 0.04, 0.01], response.Answers[1].Probabilities.ToArray());
    }

    [Fact]
    public async Task A_per_key_threshold_overrides_the_default()
    {
        // department at 0.9 is confident by the default, unsure by its own 0.95; severity at 0.5 is unsure by the
        // default, confident by its own 0.4.
        var options = Options(o =>
        {
            o.Thresholds["department"] = 0.95;
            o.Thresholds["severity"] = 0.4;
        });

        var (response, sent) = await RunAsync(Primary("severity", QuestionAnswer.Score(1, 1.0, 0.5, [0.25, 0.5, 0.25])), options);

        Assert.Equal(["department"], Keys(Assert.Single(sent).Definition));
        Assert.Equal(RouteReason.Confident, response.Path!.Routes[2].Reason);
    }

    [Fact]
    public async Task A_threshold_key_not_in_the_set_is_ignored()
    {
        var options = Options(o => o.Thresholds["not_in_this_set"] = 1.0);

        var (response, sent) = await RunAsync(SurePrimary, options);

        Assert.Empty(sent);
        Assert.All(response.Path!.Routes, route => Assert.Equal(RouteReason.Confident, route.Reason));
    }

    [Fact]
    public async Task The_re_ask_sends_the_state_and_images_with_RetryAttempt_0_and_no_Model()
    {
        var image = DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
        var request = new DecisionRequest(Triage, State) { Model = "local-2b", RetryAttempt = 2, Images = [image] };

        var (_, sent) = await RunAsync(Primary("department", QuestionAnswer.Choice(0, 0.5, [0.5, 0.4, 0.1])), request: request);

        var reAsk = Assert.Single(sent);
        Assert.Equal(State, reAsk.State);
        Assert.Equal([image], reAsk.Images);
        Assert.Equal(0, reAsk.RetryAttempt);
        Assert.Null(reAsk.Model);
        Assert.Same(Triage.Questions[1], Assert.Single(reAsk.Definition.Questions));
    }

    [Fact]
    public async Task The_escalated_answer_wins_when_less_confident()
    {
        var (response, _) = await RunAsync(Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05])));

        Assert.Equal(new QuestionRoute("department", "hosted", RouteReason.Escalated, 0.55, 0.5), response.Path!.Routes[1]);
        Assert.Equal(1, response.Answers[1].ChosenIndex);
        Assert.Equal(0.5, response.Answers[1].Confidence);
        Assert.Equal([0.3, 0.5, 0.2], response.Answers[1].Probabilities.ToArray());
    }

    [Fact]
    public async Task A_failing_target_keeps_the_primary_answers()
    {
        using var client = new EscalatingDecisionClient(
            Fake.Answering(Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]))),
            new Fake(_ => null),
            Options());

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        Assert.Equal(new QuestionRoute("department", "primary", RouteReason.TargetFailed, 0.55, null), response.Path!.Routes[1]);
        Assert.Equal(0, response.Answers[1].ChosenIndex);
        Assert.Equal([0.55, 0.4, 0.05], response.Answers[1].Probabilities.ToArray());
        Assert.Equal(RouteReason.Confident, response.Path.Routes[0].Reason);
        Assert.Equal(["primary", "hosted"], response.Path.Calls.Select(c => c.ClientName));
        Assert.Equal(new DecisionCall("hosted", null, null, IsCached: false) { Status = CallStatus.Failed, ErrorKind = DecisionErrorKind.Server }, response.Path.Calls[1]);
    }

    [Fact]
    public async Task A_failed_target_call_is_marked_Failed_with_its_kind()
    {
        using var client = new EscalatingDecisionClient(
            Fake.Answering(Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]))),
            new Fake(_ => throw new UnreachableException(), failure: new DecisionError(DecisionErrorKind.Overloaded, "busy")),
            Options());

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        Assert.Equal(
            [
                new DecisionCall("primary", null, null, IsCached: false),
                new DecisionCall("hosted", null, null, IsCached: false) { Status = CallStatus.Failed, ErrorKind = DecisionErrorKind.Overloaded },
            ],
            response.Path!.Calls);
        Assert.Equal(CallStatus.Answered, response.Path.Calls[0].Status);
        Assert.Null(response.Path.Calls[0].ErrorKind);
    }

    [Fact]
    public async Task A_mismatched_target_is_a_Failed_InvalidResponse_call()
    {
        // The call keeps the target's model and usage: the response's usage still sums its calls.
        var usage = new DecisionUsage { InputTokens = 5, OutputTokens = 1, Cost = 0.01 };
        using var client = new EscalatingDecisionClient(
            new Fake(request => Fake.Respond(request.Definition, Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05])), "local-2b", new DecisionUsage { InputTokens = 7 })),
            new Fake(request => Fake.Respond(new QuestionSetDefinition([.. request.Definition.Questions]), TargetAnswers, "hosted-model", usage)),
            Options());

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        var target = response.Path!.Calls[1];
        Assert.Equal(
            new DecisionCall("hosted", "hosted-model", usage, IsCached: false) { Status = CallStatus.Failed, ErrorKind = DecisionErrorKind.InvalidResponse },
            target);
        Assert.Equal(new DecisionUsage { InputTokens = 12, OutputTokens = 1, Cost = 0.01 }, response.Usage);
    }

    [Fact]
    public async Task A_target_answering_another_definition_is_a_target_failure()
    {
        // The target answers a definition with the same questions, but not the subset instance it was asked.
        using var client = new EscalatingDecisionClient(
            Fake.Answering(Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]))),
            new Fake(request => Fake.Respond(
                new QuestionSetDefinition([.. request.Definition.Questions]), TargetAnswers, "hosted-model", new DecisionUsage { InputTokens = 5, Cost = 0.01 })),
            Options());

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        Assert.Equal(RouteReason.TargetFailed, response.Path!.Routes[1].Reason);
        Assert.Equal("primary", response.Path.Routes[1].AnsweredBy);
        Assert.Equal(0, response.Answers[1].ChosenIndex);
        Assert.Equal(
            [new DecisionCall("primary", null, null, IsCached: false), new DecisionCall("hosted", "hosted-model", new DecisionUsage { InputTokens = 5, Cost = 0.01 }, IsCached: false) { Status = CallStatus.Failed, ErrorKind = DecisionErrorKind.InvalidResponse }],
            response.Path.Calls);
        Assert.Equal(new DecisionUsage { InputTokens = 5, Cost = 0.01 }, response.Usage);
    }

    [Fact]
    public async Task A_primary_failure_is_returned_unchanged()
    {
        var error = new DecisionError(DecisionErrorKind.Server, "down");
        var targetCalls = 0;
        using var client = new EscalatingDecisionClient(
            new Fake(_ => throw new UnreachableException(), failure: error),
            new Fake(_ => null, _ => targetCalls++),
            Options());

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Equal(0, targetCalls);
    }

    [Fact]
    public async Task An_empty_set_or_all_confident_makes_no_target_call()
    {
        var empty = new QuestionSetDefinition();
        var targetCalls = 0;
        var target = new Fake(_ => null, _ => targetCalls++);
        var primary = new Fake(
            request => Fake.Respond(request.Definition, SurePrimary, model: "local-2b", usage: new DecisionUsage { InputTokens = 7 }));
        using var client = new EscalatingDecisionClient(primary, target, Options());

        var emptyResponse = (await client.EvaluateAsync(new DecisionRequest(empty, State))).Value;
        var triage = QuestionSets.TriageDefinition();
        var sureResponse = (await client.EvaluateAsync(new DecisionRequest(triage, State))).Value;

        Assert.Equal(0, targetCalls);
        Assert.Empty(emptyResponse.Path!.Routes);
        Assert.Equal(new DecisionCall("primary", "local-2b", new DecisionUsage { InputTokens = 7 }, IsCached: false), Assert.Single(emptyResponse.Path.Calls));
        Assert.Single(sureResponse.Path!.Calls);
        Assert.Equal(
            [
                new QuestionRoute("is_urgent", "primary", RouteReason.Confident, 0.97, null),
                new QuestionRoute("department", "primary", RouteReason.Confident, 0.9, null),
                new QuestionRoute("severity", "primary", RouteReason.Confident, 0.9, null),
            ],
            sureResponse.Path.Routes);
        Assert.Equal(7, sureResponse.Usage!.InputTokens);
        Assert.Equal("local-2b", sureResponse.Model);

        // No subset definition was built for either set.
        Assert.Equal(0, EscalationMerge.CachedSubsetCount(triage));
        Assert.Equal(0, EscalationMerge.CachedSubsetCount(empty));
    }

    [Fact]
    public async Task A_target_that_throws_propagates()
    {
        var thrown = new InvalidOperationException("target broke");
        using var client = new EscalatingDecisionClient(
            Fake.Answering(Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]))),
            new Fake(_ => throw thrown),
            Options());

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => client.EvaluateAsync(new DecisionRequest(Triage, State)).AsTask());

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task The_callers_cancellation_from_the_target_propagates()
    {
        using var cts = new CancellationTokenSource();
        using var client = new EscalatingDecisionClient(
            Fake.Answering(Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]))),
            new Fake(_ =>
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return null;
            }),
            Options());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EvaluateAsync(new DecisionRequest(Triage, State), cts.Token).AsTask());
    }

    public static TheoryData<string> InvalidOptions => [.. InvalidOptionCases.Keys];

    private static readonly Dictionary<string, Action<EscalationOptions>> InvalidOptionCases = new(StringComparer.Ordinal)
    {
        ["DefaultThresholdBelowZero"] = o => o.DefaultThreshold = -0.01,
        ["DefaultThresholdAboveOne"] = o => o.DefaultThreshold = 1.01,
        ["DefaultThresholdNaN"] = o => o.DefaultThreshold = double.NaN,
        ["KeyThresholdBelowZero"] = o => o.Thresholds["department"] = -0.5,
        ["KeyThresholdAboveOne"] = o => o.Thresholds["department"] = 2,
        ["KeyThresholdNaN"] = o => o.Thresholds["department"] = double.NaN,
        ["NegativeExpectedLatency"] = o => o.ExpectedLatency = TimeSpan.FromMilliseconds(-1),
        ["ZeroBudget"] = o => o.Budget = TimeSpan.Zero,
        ["NegativeBudget"] = o => o.Budget = TimeSpan.FromSeconds(-1),
        ["NullPrimaryName"] = o => o.PrimaryName = null!,
        ["EmptyPrimaryName"] = o => o.PrimaryName = string.Empty,
        ["NullTargetName"] = o => o.TargetName = null!,
        ["EmptyTargetName"] = o => o.TargetName = string.Empty,
        ["SameNames"] = o => o.TargetName = "primary",
    };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Invalid_options_throw_ArgumentException(string name)
    {
        var options = Options(InvalidOptionCases[name]);

        var exception = Assert.ThrowsAny<ArgumentException>(() => new EscalatingDecisionClient(Fake.Answering(SurePrimary), Fake.Answering(TargetAnswers), options));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Boundary_options_are_valid()
    {
        var options = Options(o =>
        {
            o.DefaultThreshold = 0;
            o.Thresholds["department"] = 1;
            o.ExpectedLatency = TimeSpan.Zero;
            o.Budget = TimeSpan.FromTicks(1);
        });

        using var client = new EscalatingDecisionClient(Fake.Answering(SurePrimary), Fake.Answering(TargetAnswers), options);
    }

    [Fact]
    public void Options_have_the_planned_defaults()
    {
        var options = new EscalationOptions();

        Assert.Equal("primary", options.PrimaryName);
        Assert.Null(options.TargetName);
        Assert.Equal(0.6, options.DefaultThreshold);
        Assert.Empty(options.Thresholds);
        Assert.Equal(TimeSpan.Zero, options.ExpectedLatency);
        Assert.Null(options.Budget);
    }

    [Fact]
    public void Threshold_keys_compare_ordinally()
    {
        var options = new EscalationOptions();
        options.Thresholds["Department"] = 0.9;

        Assert.False(options.Thresholds.ContainsKey("department"));
    }

    [Fact]
    public async Task The_stage_copies_its_options()
    {
        var options = Options();
        using var client = new EscalatingDecisionClient(
            Fake.Answering(Primary("department", QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]))),
            Fake.Answering(TargetAnswers),
            options);

        options.DefaultThreshold = 0;
        options.Thresholds["department"] = 0;
        options.TargetName = "changed";
        var response = (await client.EvaluateAsync(new DecisionRequest(Triage, State))).Value;

        Assert.Equal(new QuestionRoute("department", "hosted", RouteReason.Escalated, 0.55, 0.5), response.Path!.Routes[1]);
    }

    [Fact]
    public void GetService_returns_the_stages_options()
    {
        var options = Options(o => o.Thresholds["department"] = 0.8);
        using var client = new EscalatingDecisionClient(Fake.Answering(SurePrimary), Fake.Answering(TargetAnswers), options);

        var found = Assert.IsType<EscalationOptions>(client.GetService(typeof(EscalationOptions)));

        Assert.Equal("hosted", found.TargetName);
        Assert.Equal(0.8, found.Thresholds["department"]);
        Assert.Null(client.GetService(typeof(EscalationOptions), "a key"));
        Assert.Same(client, client.GetService(typeof(EscalatingDecisionClient)));
    }

    /// <summary>A client that answers each request from a function, or fails when the function returns <see langword="null"/>; copied from the spike.</summary>
    internal sealed class Fake(
        Func<DecisionRequest, DecisionResponse?> answer, Action<DecisionRequest>? seen = null, DecisionError? failure = null, bool yield = false, Func<Task>? gate = null)
        : IDecisionClient
    {
        public static Fake Answering(IReadOnlyDictionary<string, QuestionAnswer> answers, Action<DecisionRequest>? seen = null)
            => new(request => Respond(request.Definition, answers), seen);

        /// <summary>A response with the answer for each of the definition's questions, looked up by key.</summary>
        public static DecisionResponse Respond(QuestionSetDefinition definition, IReadOnlyDictionary<string, QuestionAnswer> answers, string? model = null, DecisionUsage? usage = null)
            => new(definition, [.. definition.Questions.Select(q => answers[q.Key])], model, usage);

        public async ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            seen?.Invoke(request);
            if (yield)
            {
                await Task.Yield();
            }

            if (gate is not null)
            {
                await gate();
            }

            if (failure is not null)
            {
                return Result<DecisionResponse, DecisionError>.Failure(failure);
            }

            var response = answer(request);
            return response is null
                ? Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "down"))
                : Result<DecisionResponse, DecisionError>.Success(response);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
