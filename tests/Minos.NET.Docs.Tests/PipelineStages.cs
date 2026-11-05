namespace Minos.Docs.Tests;

using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Minos.Recording;
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
    // The recommended order for a pipeline you build yourself: one record and one span per call, escalation and fallback
    // above the cache so the first client's answers are cached below them (give the target and the backup their own
    // UseCaching to cache their answers too), a cache hit takes no permit, and every attempt takes one.
    public static IDecisionClient Build(
        HttpClient http, string apiKey, DecisionRecorder recorder, IDecisionClient hosted, IDecisionClient backup, RateLimiter limiter, IServiceProvider services)
    {
        var transport = new SystemOneDecisionClient(http, new SystemOneDecisionClientOptions { ApiKey = apiKey, UseStandardPipeline = false });
        return transport.AsBuilder()
            .UseRecording(recorder)
            .UseOpenTelemetry()
            .UseEscalation(hosted, options => options.TargetName = "hosted")
            .UseFallback(options => options.Steps.Add(new FallbackStep("backup", backup)))
            .UseCaching()
            .UseRetries()
            .UseRateLimiting(limiter)
            .Build(services);
    }
    #endregion
}

public static class EscalatingPipeline
{
    #region Pipeline_Escalation
    // The default client answers first. A question it is less than 0.7 sure of is asked again of "hosted", and only
    // that question: the questions it was sure of are kept. Without time for the hosted call, the first answer stands.
    public static IServiceCollection AddEscalatingDecisions(IServiceCollection services, string localKey, string hostedKey)
    {
        services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
        services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
            .UseEscalation("hosted", options =>
            {
                options.PrimaryName = "local";
                options.DefaultThreshold = 0.7;
                options.ExpectedLatency = TimeSpan.FromMilliseconds(300);
                options.Budget = TimeSpan.FromSeconds(2);
            });
        return services;
    }

    // Path says who answered each question and why. As<T> reads the merged answers as the question set's type.
    public static async Task<(ShopIntent Intent, string AnsweredBy)> ReadAsync(IDecisionClient client, string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync(new DecisionRequest(ChatMessage.Definition, DecisionContent.FromString(message)), cancellationToken);
        if (result.IsFailure)
        {
            return (ShopIntent.Other, "none");
        }

        var response = result.Value;
        return (response.As<ChatMessage>().Intent.Value, response.Path!.Routes[0].AnsweredBy);
    }
    #endregion
}
