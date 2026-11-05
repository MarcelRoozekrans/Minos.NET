using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using ZeroAlloc.Results;

namespace Minos.Tests;

public sealed class FallbackTests
{
    private static readonly QuestionSetDefinition Triage = QuestionSets.TriageDefinition();
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    // Certainties 0.97, 0.9 and 0.9.
    private static readonly Dictionary<string, QuestionAnswer> PrimaryAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.9, [0.9, 0.05, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    // Certainties 0.8, 0.7 and 0.85.
    private static readonly Dictionary<string, QuestionAnswer> StepAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.7, [0.2, 0.7, 0.1]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.85, [0.05, 0.85, 0.1]),
    };

    private static FallbackOptions Options(params FallbackStep[] steps)
    {
        var options = new FallbackOptions();
        foreach (var step in steps)
        {
            options.Steps.Add(step);
        }

        return options;
    }

    private static DecisionCall Failed(string name, DecisionErrorKind kind)
        => new(name, null, null, IsCached: false) { Status = CallStatus.Failed, ErrorKind = kind };

    private static async Task<Result<DecisionResponse, DecisionError>> EvaluateAsync(IDecisionClient primary, params FallbackStep[] steps)
    {
        using var client = new FallbackDecisionClient(primary, Options(steps));
        return await client.EvaluateAsync(new DecisionRequest(Triage, State));
    }

    [Fact]
    public async Task Step_zero_answering_passes_the_response_through_untouched()
    {
        var answer = StubDecisionClient.Respond(Triage, PrimaryAnswers, "local-2b");
        var primary = StubDecisionClient.Responding(_ => answer);
        var clef = StubDecisionClient.Answering(StepAnswers);

        var result = await EvaluateAsync(primary, new FallbackStep("clef", clef));

        Assert.True(result.IsSuccess);
        Assert.Same(answer, result.Value);
        Assert.Null(result.Value.Path);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task A_provider_failure_moves_to_the_next_step()
    {
        var usage = new DecisionUsage { InputTokens = 120, OutputTokens = 9, Cost = 0.002 };
        var clef = StubDecisionClient.Answering(StepAnswers, "clef-model", usage);

        var result = await EvaluateAsync(StubDecisionClient.Failing(DecisionErrorKind.Server), new FallbackStep("clef", clef));

        Assert.True(result.IsSuccess);
        var response = result.Value;
        Assert.Same(Triage, response.Definition);
        Assert.Equal("clef-model", response.Model);
        Assert.Equal(usage, response.Usage);
        Assert.Equal(1, response.Answers[1].ChosenIndex);
        Assert.Equal(
            [
                new QuestionRoute("is_urgent", "clef", RouteReason.FellBack, 0.8, null),
                new QuestionRoute("department", "clef", RouteReason.FellBack, 0.7, null),
                new QuestionRoute("severity", "clef", RouteReason.FellBack, 0.85, null),
            ],
            response.Path!.Routes);
        Assert.Equal(
            [Failed("primary", DecisionErrorKind.Server), new DecisionCall("clef", "clef-model", usage, IsCached: false)],
            response.Path.Calls);
    }

    [Fact]
    public async Task Each_failed_step_is_listed_before_the_answering_one()
    {
        var hosted = StubDecisionClient.Answering(StepAnswers, "hosted-model");

        var result = await EvaluateAsync(
            StubDecisionClient.Failing(DecisionErrorKind.Network),
            new FallbackStep("clef", StubDecisionClient.Failing(DecisionErrorKind.Overloaded)),
            new FallbackStep("hosted", hosted));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [
                Failed("primary", DecisionErrorKind.Network),
                Failed("clef", DecisionErrorKind.Overloaded),
                new DecisionCall("hosted", "hosted-model", null, IsCached: false),
            ],
            result.Value.Path!.Calls);
        Assert.All(result.Value.Path.Routes, route => Assert.Equal("hosted", route.AnsweredBy));
    }

    [Theory]
    [InlineData(DecisionErrorKind.Validation)]
    [InlineData(DecisionErrorKind.InvalidQuestions)]
    [InlineData(DecisionErrorKind.Refused)]
    [InlineData(DecisionErrorKind.Disposed)]
    public async Task Validation_InvalidQuestions_Refused_and_Disposed_are_returned_as_is(DecisionErrorKind kind)
    {
        var error = new DecisionError(kind, "failed");
        var clef = StubDecisionClient.Answering(StepAnswers);

        var result = await EvaluateAsync(StubDecisionClient.Failing(error), new FallbackStep("clef", clef));

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Equal(0, clef.Calls);
    }

    [Fact]
    public async Task A_later_step_whose_failure_is_rejected_is_returned_as_is()
    {
        var refusal = new DecisionError(DecisionErrorKind.Refused, "refused");
        var hosted = StubDecisionClient.Answering(StepAnswers);

        var result = await EvaluateAsync(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            new FallbackStep("clef", StubDecisionClient.Failing(refusal)),
            new FallbackStep("hosted", hosted));

        Assert.Same(refusal, result.Error);
        Assert.Equal(0, hosted.Calls);
    }

    [Fact]
    public async Task A_custom_ShouldFallBack_is_used()
    {
        var seen = new List<DecisionErrorKind>();
        var options = Options(new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers)));
        options.ShouldFallBack = error =>
        {
            seen.Add(error.Kind);
            return error.Kind == DecisionErrorKind.Refused;
        };

        using (var refusing = new FallbackDecisionClient(StubDecisionClient.Failing(DecisionErrorKind.Refused), options))
        {
            var refused = await refusing.EvaluateAsync(new DecisionRequest(Triage, State));
            Assert.True(refused.IsSuccess);
            Assert.Equal(RouteReason.FellBack, refused.Value.Path!.Routes[0].Reason);
        }

        var serverError = new DecisionError(DecisionErrorKind.Server, "down");
        using (var failing = new FallbackDecisionClient(StubDecisionClient.Failing(serverError), options))
        {
            var failed = await failing.EvaluateAsync(new DecisionRequest(Triage, State));
            Assert.Same(serverError, failed.Error);
        }

        Assert.Equal([DecisionErrorKind.Refused, DecisionErrorKind.Server], seen);
    }

    [Fact]
    public async Task A_Disposed_failure_stops_the_chain_whatever_ShouldFallBack_returns()
    {
        // The app is shutting down: like the retry stage, the chain never moves on from Disposed, and the predicate is not asked.
        var seen = new List<DecisionErrorKind>();
        bool Everything(DecisionError error)
        {
            seen.Add(error.Kind);
            return true;
        }

        var disposed = new DecisionError(DecisionErrorKind.Disposed, "disposed");
        var clef = StubDecisionClient.Answering(StepAnswers);
        var firstOptions = Options(new FallbackStep("clef", clef));
        firstOptions.ShouldFallBack = Everything;
        using (var atStepZero = new FallbackDecisionClient(StubDecisionClient.Failing(disposed), firstOptions))
        {
            var result = await atStepZero.EvaluateAsync(new DecisionRequest(Triage, State));
            Assert.Same(disposed, result.Error);
        }

        var hosted = StubDecisionClient.Answering(StepAnswers);
        var laterOptions = Options(new FallbackStep("clef", StubDecisionClient.Failing(disposed)), new FallbackStep("hosted", hosted));
        laterOptions.ShouldFallBack = Everything;
        using (var atALaterStep = new FallbackDecisionClient(StubDecisionClient.Failing(DecisionErrorKind.Server), laterOptions))
        {
            var result = await atALaterStep.EvaluateAsync(new DecisionRequest(Triage, State));
            Assert.Same(disposed, result.Error);
        }

        Assert.Equal(0, clef.Calls);
        Assert.Equal(0, hosted.Calls);
        Assert.Equal([DecisionErrorKind.Server], seen);
    }

    [Fact]
    public async Task An_exhausted_chain_returns_the_last_called_steps_error()
    {
        var last = new DecisionError(DecisionErrorKind.RateLimited, "slow down");

        var result = await EvaluateAsync(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            new FallbackStep("clef", StubDecisionClient.Failing(DecisionErrorKind.Timeout)),
            new FallbackStep("hosted", StubDecisionClient.Failing(last)));

        Assert.False(result.IsSuccess);
        Assert.Same(last, result.Error);
    }

    [Fact]
    public async Task Step_zero_gets_the_callers_request()
    {
        var primary = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var request = new DecisionRequest(Triage, State) { Model = "local-2b", RetryAttempt = 1 };
        using var client = new FallbackDecisionClient(primary, Options(new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers))));

        await client.EvaluateAsync(request);

        Assert.Same(request, Assert.Single(primary.Requests));
    }

    [Fact]
    public async Task Later_steps_get_a_new_request()
    {
        var image = DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
        var deadline = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        var request = new DecisionRequest(Triage, State) { Model = "local-2b", RetryAttempt = 2, Images = [image], Deadline = deadline };
        var clef = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var hosted = StubDecisionClient.Answering(StepAnswers);
        using var client = new FallbackDecisionClient(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            Options(new FallbackStep("clef", clef), new FallbackStep("hosted", hosted)),
            timeProvider: new FakeTimeProvider(deadline.AddSeconds(-5)));

        await client.EvaluateAsync(request);

        foreach (var sent in (DecisionRequest[])[Assert.Single(clef.Requests), Assert.Single(hosted.Requests)])
        {
            Assert.Same(Triage, sent.Definition);
            Assert.Equal(State, sent.State);
            Assert.Equal([image], sent.Images);
            Assert.Equal(0, sent.RetryAttempt);
            Assert.Null(sent.Model);
            Assert.Equal(deadline, sent.Deadline);
        }
    }

    [Fact]
    public async Task A_step_answering_another_question_set_is_a_failed_InvalidResponse_call()
    {
        var other = QuestionSets.TriageDefinition();
        var seen = new List<DecisionError>();
        var hosted = StubDecisionClient.Answering(StepAnswers, "hosted-model", new DecisionUsage { InputTokens = 50, OutputTokens = 4 });
        var options = Options(
            new FallbackStep("clef", StubDecisionClient.Responding(_ => StubDecisionClient.Respond(other, StepAnswers, "clef-model", new DecisionUsage { InputTokens = 100, Cost = 0.01 }))),
            new FallbackStep("hosted", hosted));
        options.ShouldFallBack = error =>
        {
            seen.Add(error);
            return FallbackOptions.IsProviderFailure(error);
        };
        using var client = new FallbackDecisionClient(StubDecisionClient.Failing(DecisionErrorKind.Server), options);

        var result = await client.EvaluateAsync(new DecisionRequest(Triage, State));

        Assert.True(result.IsSuccess);
        Assert.Same(Triage, result.Value.Definition);
        // The mismatch call keeps its model and usage, and the response's usage sums every call's.
        Assert.Equal(
            [
                Failed("primary", DecisionErrorKind.Server),
                new DecisionCall("clef", "clef-model", new DecisionUsage { InputTokens = 100, Cost = 0.01 }, IsCached: false) { Status = CallStatus.Failed, ErrorKind = DecisionErrorKind.InvalidResponse },
                new DecisionCall("hosted", "hosted-model", new DecisionUsage { InputTokens = 50, OutputTokens = 4 }, IsCached: false),
            ],
            result.Value.Path!.Calls);
        Assert.Equal(new DecisionUsage { InputTokens = 150, OutputTokens = 4, Cost = 0.01 }, result.Value.Usage);
        Assert.Equal("hosted-model", result.Value.Model);
        Assert.Equal(DecisionErrorKind.InvalidResponse, seen[1].Kind);
        Assert.Equal("A fallback step answered a different question set than it was asked.", seen[1].Message);
    }

    [Fact]
    public async Task A_last_step_answering_another_question_set_returns_an_InvalidResponse_error()
    {
        var other = QuestionSets.TriageDefinition();

        var result = await EvaluateAsync(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            new FallbackStep("clef", StubDecisionClient.Responding(_ => StubDecisionClient.Respond(other, StepAnswers))));

        Assert.False(result.IsSuccess);
        Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
    }

    [Fact]
    public async Task A_step_whose_response_has_a_path_is_taken_over()
    {
        // Clef's own pipeline escalates its unsure department answer to a hosted decider.
        var unsure = new Dictionary<string, QuestionAnswer>(StepAnswers, StringComparer.Ordinal)
        {
            ["department"] = QuestionAnswer.Choice(1, 0.4, [0.3, 0.4, 0.3]),
        };
        using var escalating = new EscalatingDecisionClient(
            StubDecisionClient.Answering(unsure, "clef-model"),
            StubDecisionClient.Answering(PrimaryAnswers, "hosted-model"),
            new EscalationOptions { PrimaryName = "clef", TargetName = "hosted" });

        var result = await EvaluateAsync(StubDecisionClient.Failing(DecisionErrorKind.Server), new FallbackStep("clef", escalating));

        Assert.True(result.IsSuccess);
        var path = result.Value.Path!;
        Assert.Equal(
            [
                new QuestionRoute("is_urgent", "clef", RouteReason.Confident, 0.8, null),
                new QuestionRoute("department", "hosted", RouteReason.Escalated, 0.4, 0.9),
                new QuestionRoute("severity", "clef", RouteReason.Confident, 0.85, null),
            ],
            path.Routes);
        Assert.Equal(
            [
                Failed("primary", DecisionErrorKind.Server),
                new DecisionCall("clef", "clef-model", null, IsCached: false),
                new DecisionCall("hosted", "hosted-model", null, IsCached: false),
            ],
            path.Calls);
        Assert.Equal(0, result.Value.Answers[1].ChosenIndex);
    }

    [Fact]
    public async Task Step_zero_with_a_path_passes_through_untouched()
    {
        var withPath = new DecisionResponse(Triage, [.. Triage.Questions.Select(q => PrimaryAnswers[q.Key])])
        {
            Path = new DecisionPath(
                [.. Triage.Questions.Select(q => new QuestionRoute(q.Key, "primary", RouteReason.Confident, 0.9, null))],
                [new DecisionCall("primary", null, null, IsCached: false)]),
        };

        var result = await EvaluateAsync(StubDecisionClient.Responding(_ => withPath), new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers)));

        Assert.Same(withPath, result.Value);
    }

    [Fact]
    public async Task A_step_that_throws_propagates()
    {
        var thrown = new UnreachableException("step bug");
        var hosted = StubDecisionClient.Answering(StepAnswers);

        var exception = await Assert.ThrowsAsync<UnreachableException>(() => EvaluateAsync(
            StubDecisionClient.Failing(DecisionErrorKind.Server),
            new FallbackStep("clef", new StubDecisionClient((_, _) => throw thrown)),
            new FallbackStep("hosted", hosted)));

        Assert.Same(thrown, exception);
        Assert.Equal(0, hosted.Calls);
    }

    [Fact]
    public async Task Concurrent_calls_do_not_share_paths()
    {
        // Step 0 fails every even-numbered call; each step names the call in its response's model.
        static string Marker(DecisionRequest request) => request.State.TryGetString(out var text) ? text : throw new UnreachableException();

        var primary = new StubDecisionClient(async (request, _) =>
        {
            await Task.Yield();
            var marker = Marker(request);
            return int.Parse(marker, System.Globalization.CultureInfo.InvariantCulture) % 2 == 0
                ? Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "down"))
                : Result<DecisionResponse, DecisionError>.Success(StubDecisionClient.Respond(Triage, PrimaryAnswers, marker));
        });
        var clef = new StubDecisionClient(async (request, _) =>
        {
            await Task.Yield();
            return Result<DecisionResponse, DecisionError>.Success(StubDecisionClient.Respond(Triage, StepAnswers, Marker(request)));
        });
        using var client = new FallbackDecisionClient(primary, Options(new FallbackStep("clef", clef)));

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
            (Index: i, Result: await client.EvaluateAsync(new DecisionRequest(Triage, DecisionContent.FromString(i.ToString(System.Globalization.CultureInfo.InvariantCulture))))))));

        var marker = (int i) => i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (index, result) in results)
        {
            Assert.True(result.IsSuccess);
            var response = result.Value;
            Assert.Equal(marker(index), response.Model);
            if (index % 2 != 0)
            {
                Assert.Null(response.Path);
                continue;
            }

            Assert.Equal([Failed("primary", DecisionErrorKind.Server), new DecisionCall("clef", marker(index), null, IsCached: false)], response.Path!.Calls);
            Assert.All(response.Path.Routes, route => Assert.Equal(RouteReason.FellBack, route.Reason));
        }

        Assert.Equal(50, clef.Calls);
    }

    [Fact]
    public void Dispose_disposes_the_inner_client_but_no_step()
    {
        var primary = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var clef = StubDecisionClient.Answering(StepAnswers);
        var hosted = StubDecisionClient.Answering(StepAnswers);
        var client = new FallbackDecisionClient(primary, Options(new FallbackStep("clef", clef), new FallbackStep("hosted", hosted)));

        client.Dispose();

        Assert.Equal(1, primary.Disposals);
        Assert.Equal(0, clef.Disposals);
        Assert.Equal(0, hosted.Disposals);
    }

    [Fact]
    public void GetService_returns_a_copy_of_the_options()
    {
        var options = Options(new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers)));
        using var client = new FallbackDecisionClient(StubDecisionClient.Failing(DecisionErrorKind.Server), options);

        var copy = Assert.IsType<FallbackOptions>(client.GetService(typeof(FallbackOptions)));
        copy.Steps.Clear();
        var again = Assert.IsType<FallbackOptions>(client.GetService(typeof(FallbackOptions)));

        Assert.NotSame(options, copy);
        Assert.Equal("clef", Assert.Single(again.Steps).Name);
        Assert.Null(client.GetService(typeof(FallbackOptions), "key"));
        Assert.Same(client, client.GetService(typeof(FallbackDecisionClient)));
    }

    [Fact]
    public void The_constructor_copies_and_validates_the_options()
    {
        var primary = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var options = new FallbackOptions();

        Assert.Equal("options", Assert.Throws<ArgumentException>(() => new FallbackDecisionClient(primary, options)).ParamName);
        Assert.Throws<ArgumentNullException>(() => new FallbackDecisionClient(primary, null!));
        Assert.Throws<ArgumentNullException>(() => new FallbackDecisionClient(null!, Options(new FallbackStep("clef", primary))));

        options.Steps.Add(new FallbackStep("clef", primary));
        using var client = new FallbackDecisionClient(primary, options);
        options.Steps.Clear();
        Assert.Single(((FallbackOptions)client.GetService(typeof(FallbackOptions))!).Steps);
    }
}
