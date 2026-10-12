using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Minos.Caching;

/// <summary>Adds <see cref="CachingDecisionClient"/> to a pipeline.</summary>
public static class CachingDecisionClientBuilderExtensions
{
    /// <summary>
    /// Adds a stage that answers a repeated request from an in-process <see cref="IMemoryCache"/>, then from an optional
    /// <see cref="IDistributedCache"/>, and stores each successful answer in both.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="memoryCache">The in-process cache; <see langword="null"/> to take the <see cref="IMemoryCache"/> of the services passed to <c>Build</c>.</param>
    /// <param name="distributedCache">
    /// The distributed cache, such as Redis; <see langword="null"/> to take the <see cref="IDistributedCache"/> of the services
    /// passed to <c>Build</c>, or to cache in process only when they have none.
    /// </param>
    /// <param name="configure">Changes the expiry and key prefix.</param>
    /// <returns><paramref name="builder"/>.</returns>
    /// <remarks>
    /// Register the in-process cache with <c>services.AddMemoryCache()</c>. The stage logs through the services'
    /// <see cref="ILoggerFactory"/>, when there is one. Add the stage inside the telemetry and logging stages, so they record
    /// each hit, and outside a rate-limiting stage, so a hit takes no permit.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    public static DecisionClientBuilder UseCaching(
        this DecisionClientBuilder builder,
        IMemoryCache? memoryCache = null,
        IDistributedCache? distributedCache = null,
        Action<DecisionCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use((inner, services) =>
        {
            var options = new DecisionCacheOptions();
            configure?.Invoke(options);
            var memory = memoryCache
                ?? services.GetService(typeof(IMemoryCache)) as IMemoryCache
                ?? throw new InvalidOperationException(
                    "UseCaching found no IMemoryCache: register one with services.AddMemoryCache() and pass the services to Build, or pass one to UseCaching.");
            var distributed = distributedCache ?? services.GetService(typeof(IDistributedCache)) as IDistributedCache;
            var logger = (services.GetService(typeof(ILoggerFactory)) as ILoggerFactory)?.CreateLogger(DecisionLog.Category);
            return new CachingDecisionClient(inner, memory, distributed, options, logger);
        });
    }
}
