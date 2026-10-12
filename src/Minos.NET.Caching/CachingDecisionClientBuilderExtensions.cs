using Microsoft.Extensions.Caching.Hybrid;

namespace Minos.Caching;

/// <summary>Adds <see cref="CachingDecisionClient"/> to a pipeline.</summary>
public static class CachingDecisionClientBuilderExtensions
{
    /// <summary>Adds a stage that answers a repeated request from a <see cref="HybridCache"/> and stores each successful answer.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="cache">The cache; <see langword="null"/> to take the <see cref="HybridCache"/> of the services passed to <c>Build</c>.</param>
    /// <param name="configure">Changes the expiry and key prefix.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// Register the cache with <c>services.AddHybridCache().AddDecisionSerializer()</c>. Add the stage inside the telemetry
    /// and logging stages, so they record each hit, and outside a rate-limiting stage, so a hit takes no permit.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseHybridCache(this DecisionClientBuilder builder, HybridCache? cache = null, Action<DecisionCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, services) =>
        {
            var options = new DecisionCacheOptions();
            configure?.Invoke(options);
            return new CachingDecisionClient(inner, cache ?? FromServices(services), options);
        });
    }

    private static HybridCache FromServices(IServiceProvider services)
    {
        var cache = services.GetService(typeof(HybridCache)) as HybridCache
            ?? throw new InvalidOperationException(
                "UseHybridCache found no HybridCache: register one with services.AddHybridCache().AddDecisionSerializer() and pass the services to Build, or pass a cache to UseHybridCache.");
        if (services.GetService(typeof(IHybridCacheSerializer<CachedDecision>)) is null)
        {
            throw new InvalidOperationException(
                "The HybridCache has no serializer for decisions: register it with services.AddHybridCache().AddDecisionSerializer().");
        }

        return cache;
    }
}
