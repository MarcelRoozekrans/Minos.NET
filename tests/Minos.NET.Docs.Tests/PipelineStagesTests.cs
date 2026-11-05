using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Minos.Recording;
using Minos.SystemOne;
using ZeroAlloc.Results;

namespace Minos.Docs.Tests;

// Builds each stage snippet of docs/pipeline.md and checks the stages are in the client.
public sealed class PipelineStagesTests
{
    [Fact]
    public async Task TheCachingRegistration_PutsTheCacheStageInTheRegisteredClient()
    {
        var services = new ServiceCollection();
        CachedPipeline.AddCachedDecisions(services, "docs-key");

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.NotNull(client.GetService<CachingDecisionClient>());
        Assert.NotNull(client.GetService<SystemOneDecisionClient>());
    }

    [Fact]
    public void TheRateLimitedClient_HasTheRateLimitStage()
    {
        using var http = new HttpClient();
        using var transport = new SystemOneDecisionClient(http, new SystemOneDecisionClientOptions { ApiKey = "docs-key" });
        using var client = LimitedPipeline.Build(transport);

        Assert.NotNull(client.GetService<RateLimitingDecisionClient>());
    }

    [Fact]
    public async Task TheRecommendedOrder_BuildsWithBothStages()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        await using var provider = services.BuildServiceProvider();
        using var http = new HttpClient();
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        await using var recorder = new DecisionRecorder(new InMemoryDecisionRecordSink());
        using var hosted = new Answering(Answer(0.9, 1));
        using var backup = new Answering(Answer(0.9, 1));
        using var client = OrderedPipeline.Build(http, "docs-key", recorder, hosted, backup, limiter, provider);

        Assert.NotNull(client.GetService<RecordingDecisionClient>());
        Assert.NotNull(client.GetService<EscalatingDecisionClient>());
        Assert.NotNull(client.GetService<FallbackDecisionClient>());
        Assert.NotNull(client.GetService<CachingDecisionClient>());
        Assert.NotNull(client.GetService<RateLimitingDecisionClient>());
    }

    // Runs a call through every stage of the recommended order, twice. The first client is unsure, so each call is
    // escalated to the hosted client; its first attempt fails with a 503 and is retried, and the second call's first
    // answer comes from the cache. Each transport attempt takes one permit, and a cache hit takes none.
    [Fact]
    public async Task TheRecommendedOrder_RecordsTracesEscalatesCachesRetriesAndLimitsEachCall()
    {
        using var trace = new ActivitySource("Minos.Docs.Tests.StageOrder");
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Minos" || source == trace,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span =>
            {
                lock (spans)
                {
                    spans.Add(span);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddMemoryCache();
        await using var provider = services.BuildServiceProvider();
        var handler = new ScriptedDecision.Handler([Reply.Error(503, headers: ("Retry-After", "0")), Reply.Ok(UnsureRefund)]);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://docs.example/api/") };
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        var sink = new InMemoryDecisionRecordSink();
        await using var recorder = new DecisionRecorder(sink);
        using var hosted = new Answering(Answer(0.9, 2));
        using var backup = new Answering(Answer(0.9, 1));
        using var client = OrderedPipeline.Build(http, "docs-key", recorder, hosted, backup, limiter, provider);

        ActivityTraceId traceId;
        var request = new DecisionRequest(ChatMessage.Definition, DecisionContent.FromString("I want my money back."));
        Result<DecisionResponse, DecisionError> first;
        Result<DecisionResponse, DecisionError> second;
        using (var root = trace.StartActivity("stage-order"))
        {
            traceId = root!.TraceId;
            first = await client.EvaluateAsync(request, CancellationToken.None);
            second = await client.EvaluateAsync(request, CancellationToken.None);
        }

        await recorder.FlushAsync(CancellationToken.None);

        // The hosted client answered both times; the second call's first answer came from the cache.
        Assert.Equal(ShopIntent.RequestRefund, first.Value.As<ChatMessage>().Intent.Value);
        Assert.Equal(ShopIntent.RequestRefund, second.Value.As<ChatMessage>().Intent.Value);
        Assert.False(first.Value.Path!.Calls[0].IsCached);
        Assert.True(second.Value.Path!.Calls[0].IsCached);

        // One record per call, each with both calls.
        Assert.Equal(2, sink.Records.Count);
        Assert.All(sink.Records, recorded => Assert.Equal(2, recorded.Record.Calls!.Count));

        // One span per call, each tagged with the escalated question.
        Activity[] decisions;
        lock (spans)
        {
            decisions = [.. spans.Where(span => span.Source.Name is "Minos" && span.TraceId == traceId)];
        }

        Assert.Equal(2, decisions.Length);
        Assert.All(decisions, span => Assert.Equal(1, span.GetTagItem("minos.escalation.escalated")));

        // The 503 was retried: two transport attempts, both by the first call, and one permit for each.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, limiter.GetStatistics()!.TotalSuccessfulLeases);
    }

    [Fact]
    public async Task TheEscalationRegistration_PutsTheEscalationStageInTheRegisteredClient()
    {
        var services = new ServiceCollection();
        EscalatingPipeline.AddEscalatingDecisions(services, "local-key", "hosted-key");

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();
        var options = client.GetService<EscalationOptions>();

        Assert.NotNull(client.GetService<EscalatingDecisionClient>());
        Assert.Equal("hosted", options!.TargetName);
        Assert.Equal("local", options.PrimaryName);
    }

    [Fact]
    public async Task AnUnsureQuestion_IsAnsweredByTheTargetAndThePathSaysSo()
    {
        using var client = new EscalatingDecisionClient(
            new Answering(Answer(0.4, 0)),
            new Answering(Answer(0.9, 2)),
            new EscalationOptions { PrimaryName = "local", TargetName = "hosted", DefaultThreshold = 0.7 });

        var (intent, answeredBy) = await EscalatingPipeline.ReadAsync(client, "Where is my parcel?", CancellationToken.None);

        Assert.Equal(ShopIntent.RequestRefund, intent);
        Assert.Equal("hosted", answeredBy);
    }

    [Fact]
    public async Task TheFallbackRegistration_PutsTheFallbackStageInTheRegisteredClient()
    {
        var services = new ServiceCollection();
        FallbackPipeline.AddFallbackDecisions(services, "local-key", "clef-key", "hosted-key");

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();
        var options = client.GetService<FallbackOptions>();

        Assert.NotNull(client.GetService<FallbackDecisionClient>());
        Assert.Equal(["clef", "hosted"], options!.Steps.Select(step => step.Name));
        Assert.Equal(TimeSpan.FromMilliseconds(300), options.Steps[0].ExpectedLatency);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Steps[1].ExpectedLatency);
    }

    [Fact]
    public async Task AFailingFirstClient_IsAnsweredByTheNextStepAndThePathSaysSo()
    {
        var options = new FallbackOptions { PrimaryName = "local" };
        options.Steps.Add(new FallbackStep("hosted", new Answering(Answer(0.9, 2))));
        using var client = new FallbackDecisionClient(new Failing(), options);

        var (intent, answeredBy) = await FallbackPipeline.ReadAsync(client, "Where is my parcel?", CancellationToken.None);

        Assert.Equal(ShopIntent.RequestRefund, intent);
        Assert.Equal("hosted", answeredBy);
    }

    private const string UnsureRefund = """
        {"model":"jev-1.13.0","answers":{"intent":{"type":"choice","choice":"track_order","probabilities":{"track_order":0.4,"cancel_order":0.2,"request_refund":0.2,"other":0.2},"confidence":0.4}},"usage":{"input_tokens":90,"output_tokens":12}}
        """;

    private static QuestionAnswer Answer(double confidence, int index)
        => QuestionAnswer.Choice(index, confidence, [0.25, 0.25, 0.25, 0.25]);

    private sealed class Failing : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Overloaded, "The provider is busy.")));

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class Answering(QuestionAnswer answer) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, [answer])));

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
