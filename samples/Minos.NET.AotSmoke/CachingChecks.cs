using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Minos.AotSmoke;

/// <summary>The caching stage under Native AOT: a miss and a hit in the in-process cache, the identity of the stored response, a read served by the distributed level to a cold in-process cache, and the options a hand-built client keeps.</summary>
internal static class CachingChecks
{
    [Covers("Minos.CachingDecisionClient.CachingDecisionClient(Minos.IDecisionClient! innerClient, Microsoft.Extensions.Caching.Memory.IMemoryCache! memoryCache, Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributedCache = null, Minos.DecisionCacheOptions? options = null, Microsoft.Extensions.Logging.ILogger? logger = null) -> void")]
    [Covers("Minos.DecisionCacheOptions.DecisionCacheOptions() -> void")]
    [Covers("override Minos.CachingDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("static Minos.CachingDecisionClientBuilderExtensions.UseCaching(this Minos.DecisionClientBuilder! builder, Microsoft.Extensions.Caching.Memory.IMemoryCache? memoryCache = null, Microsoft.Extensions.Caching.Distributed.IDistributedCache? distributedCache = null, System.Action<Minos.DecisionCacheOptions!>? configure = null) -> Minos.DecisionClientBuilder!")]
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
        var layeredInner = new FixedResponseClient(PipelineChecks.TriageResponse());
        using var layered = layeredInner
            .AsBuilder()
            .UseCaching(memory, distributed)
            .Build();
        var layeredMiss = await layered.EvaluateAsync(request).ConfigureAwait(false);
        var layeredHit = await layered.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            layeredMiss.IsSuccess && !layeredMiss.Value.IsCached && layeredHit.IsSuccess && layeredHit.Value.IsCached && layeredInner.Calls == 1,
            "the caching stage writes through to a distributed cache and answers the repeat from the in-process level under Native AOT");

        // A fresh in-process cache over the same distributed one: the answer can only come from the distributed level.
        using var coldMemory = new MemoryCache(new MemoryCacheOptions());
        var coldInner = new FixedResponseClient(PipelineChecks.TriageResponse());
        using var cold = coldInner
            .AsBuilder()
            .UseCaching(coldMemory, distributed)
            .Build();
        var fromDistributed = await cold.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            fromDistributed.IsSuccess && fromDistributed.Value.IsCached && coldInner.Calls == 0,
            "the caching stage answers from the distributed cache, without calling the inner client, when the in-process cache is cold under Native AOT");

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
