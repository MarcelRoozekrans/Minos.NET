using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minos.Recording;
using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// The escalation stage under Native AOT: an unsure question re-asked of the target, a skip for lack of time, a target
/// that fails, and the path each of them leaves on the response; and the path written to and read back from a record
/// line and a distributed cache entry, the formats' newest versions.
/// </summary>
internal static class EscalationChecks
{
    [Covers("Minos.DecisionResponse.As<T>() -> T")]
    [Covers("Minos.EscalatingDecisionClient.EscalatingDecisionClient(Minos.IDecisionClient! innerClient, Minos.IDecisionClient! target, Minos.EscalationOptions! options, Microsoft.Extensions.Logging.ILogger? logger = null, System.TimeProvider? timeProvider = null) -> void")]
    [Covers("Minos.EscalationOptions.EscalationOptions() -> void")]
    [Covers("override Minos.EscalatingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("override Minos.EscalatingDecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    [Covers("static Minos.EscalatingDecisionClientBuilderExtensions.UseEscalation(this Minos.DecisionClientBuilder! builder, Minos.IDecisionClient! target, System.Action<Minos.EscalationOptions!>! configure) -> Minos.DecisionClientBuilder!")]
    public static async Task AnUnsureQuestionIsAskedOfTheTarget()
    {
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        var primary = new AnswerClient(UnsurePrimary);
        var target = new AnswerClient(Target);
        var options = new EscalationOptions { PrimaryName = "local", TargetName = "hosted", DefaultThreshold = 0.6 };
        using var direct = new EscalatingDecisionClient(primary, target, options);
        var service = direct.GetService(typeof(EscalationOptions)) as EscalationOptions;

        var directResult = await direct.EvaluateAsync(request).ConfigureAwait(false);
        using var built = new AnswerClient(UnsurePrimary).AsBuilder()
            .UseEscalation(target, o =>
            {
                o.TargetName = "hosted";
                o.PrimaryName = "local";
            })
            .Build();
        var builtResult = await built.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            service is { TargetName: "hosted", PrimaryName: "local" } && !ReferenceEquals(service, options),
            "an escalating stage offers a copy of its options under Native AOT");
        Program.Check(
            directResult.IsSuccess && IsEscalated(directResult.Value) && builtResult.IsSuccess && IsEscalated(builtResult.Value),
            "an unsure question is re-asked of the target and its route is Escalated, for a stage built by hand and through the builder, under Native AOT");

        var answers = directResult.Value.As<SmokeTriage>();
        Program.Check(
            answers.Team.Value == Team.Billing && answers.Urgency.Value == Urgency.High && !answers.RequestsCredentials.Value,
            "As reads the merged answers, the target's for the unsure question and the primary's for the rest, under Native AOT");
    }

    [Covers("static Minos.EscalatingDecisionClientBuilderExtensions.UseEscalation(this Minos.DecisionClientBuilder! builder, System.Func<System.IServiceProvider!, Minos.IDecisionClient!>! target, System.Action<Minos.EscalationOptions!>! configure) -> Minos.DecisionClientBuilder!")]
    public static async Task NoTimeLeftSkipsTheTarget()
    {
        var target = new AnswerClient(Target);
        using var client = new AnswerClient(UnsurePrimary).AsBuilder()
            .UseEscalation(_ => target, o => o.TargetName = "hosted")
            .Build(new ServiceCollection().BuildServiceProvider());
        var pastDeadline = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State) { Deadline = DateTimeOffset.UtcNow.AddMinutes(-1) };

        var result = await client.EvaluateAsync(pastDeadline).ConfigureAwait(false);

        var path = result.IsSuccess ? result.Value.Path : null;
        Program.Check(
            path is { Routes: [{ Reason: RouteReason.Confident }, { Reason: RouteReason.NoTimeLeft, AnsweredBy: "primary" }, { Reason: RouteReason.Confident }] }
                && path.Calls is [{ ClientName: "primary", Status: CallStatus.Answered }, { ClientName: "hosted", Status: CallStatus.NotCalledNoTimeLeft }]
                && target.Calls == 0,
            "an unsure question with no time left keeps the primary answer, routes NoTimeLeft, calls no target and records the skipped call, under Native AOT");
    }

    [Covers("static Minos.KeyedEscalationDecisionClientBuilderExtensions.UseEscalation(this Minos.DecisionClientBuilder! builder, string! targetKey, System.Action<Minos.EscalationOptions!>? configure = null) -> Minos.DecisionClientBuilder!")]
    public static async Task AFailingTargetKeepsThePrimaryAnswer()
    {
        var failing = new AnswerClient(_ => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Overloaded, "The target is busy.")));
        var services = new ServiceCollection();
        services.AddDecisionClient("hosted", _ => failing);
        services.AddDecisionClient(_ => new AnswerClient(UnsurePrimary)).UseEscalation("hosted");
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        var result = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);

        var path = result.IsSuccess ? result.Value.Path : null;
        Program.Check(
            path is { Routes: [{ Reason: RouteReason.Confident }, { Reason: RouteReason.TargetFailed, AnsweredBy: "primary" }, { Reason: RouteReason.Confident }] }
                && path.Calls is [{ ClientName: "primary" }, { ClientName: "hosted" }]
                && failing.Calls == 1
                && result.Value.Answers[1].ChosenIndex == 1,
            "a target that fails leaves the primary answer, routes TargetFailed and still counts the call, for a target resolved by key, under Native AOT");
    }

    [Covers("Minos.DecisionCall.DecisionCall(string! ClientName, string? Model, Minos.DecisionUsage? Usage, bool IsCached) -> void")]
    [Covers("Minos.DecisionPath.DecisionPath(System.Collections.Generic.IReadOnlyList<Minos.QuestionRoute>! routes, System.Collections.Generic.IReadOnlyList<Minos.DecisionCall!>! calls) -> void")]
    [Covers("Minos.QuestionRoute.Equals(Minos.QuestionRoute other) -> bool")]
    [Covers("Minos.QuestionRoute.QuestionRoute() -> void")]
    [Covers("Minos.QuestionRoute.QuestionRoute(string! key, string! answeredBy, Minos.RouteReason reason, double primaryCertainty, double? targetCertainty) -> void")]
    public static void PathValuesAreKeptAndCompared()
    {
        var route = new QuestionRoute("team", "local", RouteReason.Escalated, 0.3, 0.9);
        var same = new QuestionRoute("team", "local", RouteReason.Escalated, 0.3, 0.9);
        var other = new QuestionRoute("team", "local", RouteReason.TargetFailed, 0.3, null);
        var call = new DecisionCall("local", "jev-1.13.0", null, IsCached: false);
        var path = new DecisionPath(
            [
                new QuestionRoute("requests_credentials", "local", RouteReason.Confident, 0.9, null),
                route,
                new QuestionRoute("urgency", "local", RouteReason.Confident, 0.8, null),
            ],
            [call]);
        var response = new DecisionResponse(PipelineChecks.TriageResponse().Definition, [QuestionAnswer.Noul(0.1), QuestionAnswer.Choice(1, 0.7, [0.2, 0.8]), QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9])])
        {
            Path = path,
        };

        Program.Check(
            route.Equals(same) && route == same && !route.Equals(other) && route != other,
            "routes compare by value under Native AOT");
        Program.Check(
            new QuestionRoute().Key is null
                && ReferenceEquals(response.Path, path)
                && path.Routes[1].Equals(route)
                && path.Calls is [{ ClientName: "local", Model: "jev-1.13.0", IsCached: false }],
            "a hand-built path keeps its routes and calls and rides on a response under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => _ = new DecisionPath([], []))
                && SmokeAssert.Throws<ArgumentException>(() => _ = new DecisionPath([default], [call])),
            "a path without calls, or with a default route, is refused under Native AOT");
    }

    public static async Task AnEscalatedRecordLineRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-escalated-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var recorder = new DecisionRecorder(new JsonlDecisionRecordSink(path));
            using (var client = new AnswerClient(UnsurePrimary).AsBuilder()
                .UseRecording(recorder)
                .UseEscalation(new AnswerClient(Target), o =>
                {
                    o.TargetName = "hosted";
                    o.PrimaryName = "local";
                })
                .Build())
            {
                _ = await client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).ConfigureAwait(false);
            }

            await recorder.DisposeAsync().ConfigureAwait(false);

            var problems = new List<DecisionRecordFileProblem>();
            var read = new List<RecordedDecision>();
            await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, CancellationToken.None).ConfigureAwait(false))
            {
                read.Add(decision);
            }

            var record = read.Count == 1 ? read[0].Record : null;
            Program.Check(
                problems.Count == 0
                    && record is
                    {
                        Calls: [{ ClientName: "local", Model: "jev-1.13.0", IsCached: false }, { ClientName: "hosted", Model: "hosted-model", IsCached: false }],
                        Answers: [{ Reason: RouteReason.Confident, AnsweredBy: "local" }, { Reason: RouteReason.Escalated, AnsweredBy: "hosted", PrimaryCertainty: 0.3, TargetCertainty: 0.9 }, { Reason: RouteReason.Confident }],
                    },
                "an escalated decision is written as a record line with its calls and routes and read back, under Native AOT");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static async Task AnEscalatedCacheEntryRoundTrips()
    {
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var memory = new MemoryCache(new MemoryCacheOptions());
        using var writer = new AnswerClient(UnsurePrimary).AsBuilder()
            .UseCaching(memory, distributed)
            .UseEscalation(new AnswerClient(Target), o => o.TargetName = "hosted")
            .Build();
        var miss = await writer.EvaluateAsync(request).ConfigureAwait(false);

        // A cold in-process cache over the same distributed one: the path can only come from the stored entry.
        using var coldMemory = new MemoryCache(new MemoryCacheOptions());
        var primary = new AnswerClient(UnsurePrimary);
        using var reader = primary.AsBuilder()
            .UseCaching(coldMemory, distributed)
            .UseEscalation(new AnswerClient(Target), o => o.TargetName = "hosted")
            .Build();
        var hit = await reader.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            miss.IsSuccess && !miss.Value.IsCached
                && hit.IsSuccess && hit.Value.IsCached && primary.Calls == 0
                && hit.Value.Path is
                {
                    Routes: [{ Reason: RouteReason.Confident }, { Reason: RouteReason.Escalated, AnsweredBy: "hosted", PrimaryCertainty: 0.3, TargetCertainty: 0.9 }, { Reason: RouteReason.Confident }],
                    Calls: [{ ClientName: "primary", Model: "jev-1.13.0", IsCached: false }, { ClientName: "hosted", Model: "hosted-model", IsCached: false }],
                }
                && hit.Value.Answers[1].ChosenIndex == 0,
            "an escalated response is stored in the distributed cache with its path and read back by a cold cache, under Native AOT");
    }

    // Team below the default threshold of 0.6, the other two questions above it.
    private static Result<DecisionResponse, DecisionError> UnsurePrimary(DecisionRequest request)
        => Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(
            request.Definition,
            [QuestionAnswer.Noul(0.1), QuestionAnswer.Choice(1, 0.3, [0.4, 0.6]), QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9])],
            "jev-1.13.0"));

    // Answers the questions it is sent, which are the unsure subset: team is billing, with confidence.
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

        return Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, answers, "hosted-model"));
    }

    private static bool IsEscalated(DecisionResponse response)
        => response.Path is
        {
            Routes: [{ Reason: RouteReason.Confident }, { Reason: RouteReason.Escalated, AnsweredBy: "hosted", PrimaryCertainty: 0.3, TargetCertainty: 0.9 }, { Reason: RouteReason.Confident }],
            Calls: [{ ClientName: "local", Model: "jev-1.13.0" }, { ClientName: "hosted", Model: "hosted-model" }],
        };

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
