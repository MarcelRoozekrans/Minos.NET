namespace Minos.Docs.Tests;

using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Minos.Caching;
using Minos.RateLimiting;
using Minos.SystemOne;

public static class CachedPipeline
{
    #region Pipeline_Caching
    // Answers repeated decisions from memory, and from Redis or any IDistributedCache the app registers.
    public static IServiceCollection AddCachedDecisions(IServiceCollection services, string apiKey)
    {
        services.AddMemoryCache();
        services.AddSystemOneDecisionClient(options => options.ApiKey = apiKey)
            .UseCaching(configure: cache => cache.Expiration = TimeSpan.FromHours(1));
        return services;
    }
    #endregion
}

public static class LimitedPipeline
{
    #region Pipeline_RateLimiting
    // At most 10 calls a second, shared by every client given the same limiter. A call waits up to 20 deep in the
    // queue; beyond that it fails with RateLimited and nothing is sent.
    public static readonly RateLimiter SharedLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 10,
        TokensPerPeriod = 10,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 20,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public static IDecisionClient Build(SystemOneDecisionClient client) => client.AsBuilder().UseRateLimiting(SharedLimiter).Build();
    #endregion
}

public static class OrderedPipeline
{
    #region Pipeline_StageOrder
    // The recommended order for a pipeline you build yourself: a cache hit takes no permit, and every attempt takes one.
    public static IDecisionClient Build(HttpClient http, string apiKey, RateLimiter limiter, IServiceProvider services)
    {
        var transport = new SystemOneDecisionClient(http, new SystemOneDecisionClientOptions { ApiKey = apiKey, UseStandardPipeline = false });
        return transport.AsBuilder()
            .UseOpenTelemetry()
            .UseCaching()
            .UseRetries()
            .UseRateLimiting(limiter)
            .Build(services);
    }
    #endregion
}
