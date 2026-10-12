using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minos.Caching;

namespace Minos.AotSmoke;

/// <summary>The caching stage under Native AOT: a miss, a hit, the distributed path, and the options.</summary>
internal static class CachingChecks
{
    [Covers("Minos.Caching.CachingDecisionClient.CachingDecisionClient(Minos.IDecisionClient! innerClient, Microsoft.Extensions.Caching.Memory.IMemoryCache! memoryCache, Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributedCache = null, Minos.Caching.DecisionCacheOptions? options = null, Microsoft.Extensions.Logging.ILogger? logger = null) -> void")]
    [Covers("Minos.Caching.DecisionCacheOptions.DecisionCacheOptions() -> void")]
    [Covers("override Minos.Caching.CachingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("static Minos.Caching.CachingDecisionClientBuilderExtensions.UseCaching(this Minos.DecisionClientBuilder! builder, Microsoft.Extensions.Caching.Memory.IMemoryCache? memoryCache = null, Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributedCache = null, System.Action<Minos.Caching.DecisionCacheOptions!>? configure = null) -> Minos.DecisionClientBuilder!")]
    public static async Task RepeatIsAnsweredFromTheCache()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        using var provider = services.BuildServiceProvider();
        var request = new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State);

        using var client = new FixedResponseClient(PipelineChecks.TriageResponse())
            .AsBuilder()
            .UseCaching(configure: o =>
            {
                o.KeyPrefix = "smoke:";
                o.Expiration = TimeSpan.FromMinutes(1);
                o.LocalCacheExpiration = TimeSpan.FromMinutes(1);
            })
            .Build(provider);
        var first = await client.EvaluateAsync(request).ConfigureAwait(false);
        var second = await client.EvaluateAsync(request).ConfigureAwait(false);
        var third = await client.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            first.IsSuccess && !first.Value.IsCached && second.IsSuccess && second.Value.IsCached,
            "the caching stage answers a repeated request from the cache and marks it IsCached under Native AOT");

        // The in-process cache keeps the stored entry, so two hits return one response object.
        Program.Check(
            third.IsSuccess && third.Value.IsCached && ReferenceEquals(second.Value, third.Value),
            "two in-process cache hits return the same DecisionResponse instance under Native AOT");

        using var memory = new MemoryCache(new MemoryCacheOptions());
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        using var layered = new FixedResponseClient(PipelineChecks.TriageResponse())
            .AsBuilder()
            .UseCaching(memory, distributed)
            .Build();
        var layeredMiss = await layered.EvaluateAsync(request).ConfigureAwait(false);
        var layeredHit = await layered.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            layeredMiss.IsSuccess && !layeredMiss.Value.IsCached && layeredHit.IsSuccess && layeredHit.Value.IsCached,
            "the caching stage writes through to a distributed cache and answers the repeat from the layers under Native AOT");

        var options = new DecisionCacheOptions
        {
            KeyPrefix = "direct:",
            Expiration = TimeSpan.FromMinutes(2),
            LocalCacheExpiration = TimeSpan.FromMinutes(3),
        };
        using var direct = new CachingDecisionClient(
            new FixedResponseClient(PipelineChecks.TriageResponse()),
            provider.GetRequiredService<IMemoryCache>(),
            distributedCache: null,
            options: options);
        var miss = await direct.EvaluateAsync(request).ConfigureAwait(false);
        var hit = await direct.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            string.Equals(options.KeyPrefix, "direct:", StringComparison.Ordinal)
                && options.Expiration == TimeSpan.FromMinutes(2)
                && options.LocalCacheExpiration == TimeSpan.FromMinutes(3)
                && miss.IsSuccess && !miss.Value.IsCached && hit.IsSuccess && hit.Value.IsCached,
            "a CachingDecisionClient built by hand keeps its options and caches under its own prefix under Native AOT");
    }
}
