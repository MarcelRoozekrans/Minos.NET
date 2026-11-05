using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// The fallback stage under Native AOT: a step that answers after the first client fails, a step skipped for lack of time,
/// a chain that runs out of steps, a failure that does not move on, steps resolved by key, and escalation over a fallback;
/// and the path each of them leaves on the response.
/// </summary>
internal static class FallbackChecks
{
    [Covers("Minos.FallbackDecisionClient.FallbackDecisionClient(Minos.IDecisionClient! innerClient, Minos.FallbackOptions! options, Microsoft.Extensions.Logging.ILogger? logger = null, System.TimeProvider? timeProvider = null) -> void")]
    [Covers("Minos.FallbackOptions.FallbackOptions() -> void")]
    [Covers("Minos.FallbackStep.FallbackStep(string! name, Minos.IDecisionClient! client) -> void")]
    [Covers("override Minos.FallbackDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("override Minos.FallbackDecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    [Covers("static Minos.FallbackDecisionClientBuilderExtensions.UseFallback(this Minos.DecisionClientBuilder! builder, System.Action<Minos.FallbackOptions!>! configure) -> Minos.DecisionClientBuilder!")]
    [Covers("static Minos.FallbackOptions.IsProviderFailure(Minos.DecisionError! error) -> bool")]
    public static async Task AFailingFirstClientFallsBackToTheNextStep()
    {
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        var options = new FallbackOptions { PrimaryName = "local" };
        options.Steps.Add(new FallbackStep("hosted", new AnswerClient(Answer)));
        using var direct = new FallbackDecisionClient(new AnswerClient(Overloaded), options);
        var service = direct.GetService(typeof(FallbackOptions)) as FallbackOptions;

        var directResult = await direct.EvaluateAsync(request).ConfigureAwait(false);
        using var built = new AnswerClient(Overloaded).AsBuilder()
            .UseFallback(o =>
            {
                o.PrimaryName = "local";
                o.Steps.Add(new FallbackStep("hosted", new AnswerClient(Answer)));
            })
            .Build();
        var builtResult = await built.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            service is { PrimaryName: "local", Steps.Count: 1 } && !ReferenceEquals(service, options),
            "a fallback stage offers a copy of its options under Native AOT");
        Program.Check(
            directResult.IsSuccess && FellBackToHosted(directResult.Value) && builtResult.IsSuccess && FellBackToHosted(builtResult.Value),
            "a failing first client falls back to the next step, which every question is routed FellBack to, for a stage built by hand and through the builder, under Native AOT");
        Program.Check(
            FallbackOptions.IsProviderFailure(new DecisionError(DecisionErrorKind.Overloaded, "Busy."))
                && !FallbackOptions.IsProviderFailure(new DecisionError(DecisionErrorKind.Refused, "No.")),
            "a provider failure moves the chain on and a refusal does not, under Native AOT");
    }

    [Covers("static Minos.FallbackDecisionClientBuilderExtensions.UseFallback(this Minos.DecisionClientBuilder! builder, System.Func<System.IServiceProvider!, Minos.FallbackOptions!>! configure) -> Minos.DecisionClientBuilder!")]
    public static async Task OptionsBuiltFromTheServicesFallBack()
    {
        using var client = new AnswerClient(Overloaded).AsBuilder()
            .UseFallback(_ =>
            {
                var options = new FallbackOptions { PrimaryName = "local" };
                options.Steps.Add(new FallbackStep("hosted", new AnswerClient(Answer)));
                return options;
            })
            .Build(new ServiceCollection().BuildServiceProvider());

        var result = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);

        Program.Check(
            result.IsSuccess && FellBackToHosted(result.Value),
            "a fallback stage whose options are built from the services falls back, under Native AOT");
    }

    public static async Task AStepThatDoesNotFitTheTimeLeftIsSkipped()
    {
        var slow = new AnswerClient(Answer);
        var options = new FallbackOptions { PrimaryName = "local" };
        options.Steps.Add(new FallbackStep("slow", slow) { ExpectedLatency = TimeSpan.FromMinutes(5) });
        options.Steps.Add(new FallbackStep("hosted", new AnswerClient(Answer)));
        using var client = new FallbackDecisionClient(new AnswerClient(Overloaded), options);
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State) { Deadline = DateTimeOffset.UtcNow.AddMinutes(1) };

        var result = await client.EvaluateAsync(request).ConfigureAwait(false);

        var path = result.IsSuccess ? result.Value.Path : null;
        Program.Check(
            path is
            {
                Calls: [
                    { ClientName: "local", Status: CallStatus.Failed, ErrorKind: DecisionErrorKind.Overloaded },
                    { ClientName: "slow", Status: CallStatus.NotCalledNoTimeLeft },
                    { ClientName: "hosted", Status: CallStatus.Answered, Model: "hosted-model" },
                ],
            }
                && path.Routes.All(route => route is { Reason: RouteReason.FellBack, AnsweredBy: "hosted" })
                && slow.Calls == 0,
            "a step that does not fit the time left is skipped and not called, and the next step answers, under Native AOT");
    }

    public static async Task AnExhaustedChainReturnsTheLastError()
    {
        var options = new FallbackOptions { PrimaryName = "local" };
        options.Steps.Add(new FallbackStep("backup", new AnswerClient(Overloaded)));
        options.Steps.Add(new FallbackStep("hosted", new AnswerClient(_ => Failure(DecisionErrorKind.Timeout))));
        using var client = new FallbackDecisionClient(new AnswerClient(Overloaded), options);

        var result = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);

        Program.Check(
            result.IsFailure && result.Error.Kind == DecisionErrorKind.Timeout,
            "a chain whose every step fails returns the error of the last step, under Native AOT");
    }

    public static async Task ARefusalDoesNotMoveOn()
    {
        var next = new AnswerClient(Answer);
        var options = new FallbackOptions();
        options.Steps.Add(new FallbackStep("hosted", next));
        using var client = new FallbackDecisionClient(new AnswerClient(_ => Failure(DecisionErrorKind.Refused)), options);

        var result = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);

        Program.Check(
            result.IsFailure && result.Error.Kind == DecisionErrorKind.Refused && next.Calls == 0,
            "a refusal is returned as it is and no step is asked, under Native AOT");
    }

    [Covers("Minos.KeyedFallbackStep.KeyedFallbackStep() -> void")]
    [Covers("Minos.KeyedFallbackStep.KeyedFallbackStep(string! Key, System.TimeSpan ExpectedLatency) -> void")]
    [Covers("static Minos.KeyedFallbackDecisionClientBuilderExtensions.UseFallback(this Minos.DecisionClientBuilder! builder, params Minos.KeyedFallbackStep[]! steps) -> Minos.DecisionClientBuilder!")]
    [Covers("static Minos.KeyedFallbackDecisionClientBuilderExtensions.UseFallback(this Minos.DecisionClientBuilder! builder, System.Action<Minos.FallbackOptions!>! configure, params Minos.KeyedFallbackStep[]! steps) -> Minos.DecisionClientBuilder!")]
    public static async Task StepsAreResolvedByKey()
    {
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        var step = new KeyedFallbackStep("hosted", TimeSpan.FromMilliseconds(300));

        var services = new ServiceCollection();
        services.AddDecisionClient("hosted", _ => new AnswerClient(Answer));
        services.AddDecisionClient(_ => new AnswerClient(Overloaded)).UseFallback(step);
        using var provider = services.BuildServiceProvider();
        var plain = await provider.GetRequiredService<IDecisionClient>().EvaluateAsync(request).ConfigureAwait(false);

        var named = new ServiceCollection();
        named.AddDecisionClient("hosted", _ => new AnswerClient(Answer));
        named.AddDecisionClient(_ => new AnswerClient(Overloaded)).UseFallback(o => o.PrimaryName = "local", step);
        using var namedProvider = named.BuildServiceProvider();
        var withName = await namedProvider.GetRequiredService<IDecisionClient>().EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            new KeyedFallbackStep().Key is null
                && string.Equals(step.Key, "hosted", StringComparison.Ordinal)
                && step.ExpectedLatency == TimeSpan.FromMilliseconds(300)
                && plain.IsSuccess && plain.Value.Path is { Calls: [{ ClientName: "primary", Status: CallStatus.Failed }, { ClientName: "hosted", Status: CallStatus.Answered }] }
                && withName.IsSuccess && withName.Value.Path is { Calls: [{ ClientName: "local" }, { ClientName: "hosted" }] },
            "a fallback step resolved by key answers after the first client fails, with the name the options give the first client, under Native AOT");
    }

    public static async Task EscalationOverAFallbackFlattensThePath()
    {
        var target = new AnswerClient(Target);
        using var client = new AnswerClient(Overloaded).AsBuilder()
            .UseEscalation(target, o =>
            {
                o.PrimaryName = "chain";
                o.TargetName = "strong";
            })
            .UseFallback(o =>
            {
                o.PrimaryName = "local";
                o.Steps.Add(new FallbackStep("hosted", new AnswerClient(UnsureAnswer)));
            })
            .Build();

        var result = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);

        var path = result.IsSuccess ? result.Value.Path : null;
        Program.Check(
            path is
            {
                Routes: [{ Reason: RouteReason.Confident }, { Reason: RouteReason.Escalated, AnsweredBy: "strong" }, { Reason: RouteReason.Confident }],
                Calls: [
                    { ClientName: "local", Status: CallStatus.Failed },
                    { ClientName: "hosted", Status: CallStatus.Answered },
                    { ClientName: "strong", Status: CallStatus.Answered },
                ],
            }
                && target.Calls == 1,
            "escalation over a fallback leaves one flat list of every client tried, and a question the outer stage finds sure reads Confident, under Native AOT");
    }

    private static bool FellBackToHosted(DecisionResponse response)
        => response.Path is
        {
            Routes: [{ Reason: RouteReason.FellBack, AnsweredBy: "hosted" }, { Reason: RouteReason.FellBack, AnsweredBy: "hosted" }, { Reason: RouteReason.FellBack, AnsweredBy: "hosted" }],
            Calls: [{ ClientName: "local", Status: CallStatus.Failed, ErrorKind: DecisionErrorKind.Overloaded }, { ClientName: "hosted", Status: CallStatus.Answered, Model: "hosted-model" }],
        };

    private static Result<DecisionResponse, DecisionError> Failure(DecisionErrorKind kind)
        => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(kind, "The client failed."));

    private static Result<DecisionResponse, DecisionError> Overloaded(DecisionRequest request) => Failure(DecisionErrorKind.Overloaded);

    // Every question answered with confidence.
    private static Result<DecisionResponse, DecisionError> Answer(DecisionRequest request)
        => Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
            request.Definition,
            [QuestionAnswer.Noul(0.1), QuestionAnswer.Choice(0, 0.9, [0.9, 0.1]), QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9])],
            "hosted-model"));

    // The team question below the default threshold of 0.6, the other two above it.
    private static Result<DecisionResponse, DecisionError> UnsureAnswer(DecisionRequest request)
        => Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
            request.Definition,
            [QuestionAnswer.Noul(0.1), QuestionAnswer.Choice(1, 0.3, [0.4, 0.6]), QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9])],
            "hosted-model"));

    // Answers the questions it is sent, which are the unsure subset, with confidence.
    private static Result<DecisionResponse, DecisionError> Target(DecisionRequest request)
    {
        var questions = request.Definition.Questions;
        var answers = new QuestionAnswer[questions.Count];
        for (var i = 0; i < answers.Length; i++)
        {
            answers[i] = questions[i].Kind switch
            {
                QuestionKind.Noul => QuestionAnswer.Noul(0.05),
                QuestionKind.Choice => QuestionAnswer.Choice(0, 0.9, [0.9, 0.1]),
                _ => QuestionAnswer.Score(0, 0.1, 0.9, [0.9, 0.05, 0.05]),
            };
        }

        return Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, answers, "strong-model"));
    }

    /// <summary>A client that answers each call with the function it is given, counting the calls.</summary>
    private sealed class AnswerClient(Func<DecisionRequest, Result<DecisionResponse, DecisionError>> answer) : IDecisionClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return new(answer(request));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
