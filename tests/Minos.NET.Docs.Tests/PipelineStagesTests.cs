using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Minos.Caching;
using Minos.RateLimiting;
using Minos.SystemOne;

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
        Assert.NotNull(client.GetService<SystemOneClient>());
    }

    [Fact]
    public void TheRateLimitedClient_HasTheRateLimitStage()
    {
        using var http = new HttpClient();
        using var transport = new SystemOneClient(http, new SystemOneClientOptions { ApiKey = "docs-key" });
        using var client = LimitedPipeline.Build(transport);

        Assert.NotNull(client.GetService<RateLimitingDecisionClient>());
    }

    [Fact]
    public async Task TheRecommendedOrder_BuildsWithBothStages()
    {
        var services = new ServiceCollection();
        services.AddHybridCache().AddDecisionSerializer();
        await using var provider = services.BuildServiceProvider();
        using var http = new HttpClient();
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = 1, QueueLimit = 0 });
        using var client = OrderedPipeline.Build(http, "docs-key", limiter, provider);

        Assert.NotNull(client.GetService<CachingDecisionClient>());
        Assert.NotNull(client.GetService<RateLimitingDecisionClient>());
    }
}
