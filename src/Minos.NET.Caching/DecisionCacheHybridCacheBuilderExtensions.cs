using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Minos.Caching;

/// <summary>Registers the serializer the decision cache stage stores its entries with.</summary>
public static class DecisionCacheHybridCacheBuilderExtensions
{
    /// <summary>Registers the decision serializer, so the distributed cache stores answers as compact JSON, with no reflection.</summary>
    /// <param name="builder">The builder <c>AddHybridCache()</c> returned.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static IHybridCacheBuilder AddDecisionSerializer(this IHybridCacheBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSerializer<CachedDecision, CachedDecisionSerializer>();
    }
}
