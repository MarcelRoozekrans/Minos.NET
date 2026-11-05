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
        using var client = OrderedPipeline.Build(http, "docs-key", recorder, hosted, limiter, provider);

        Assert.NotNull(client.GetService<RecordingDecisionClient>());
        Assert.NotNull(client.GetService<EscalatingDecisionClient>());
        Assert.NotNull(client.GetService<CachingDecisionClient>());
        Assert.NotNull(client.GetService<RateLimitingDecisionClient>());
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

    private static QuestionAnswer Answer(double confidence, int index)
        => QuestionAnswer.Choice(index, confidence, [0.25, 0.25, 0.25, 0.25]);

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
