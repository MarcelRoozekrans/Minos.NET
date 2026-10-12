using Microsoft.Extensions.Caching.Hybrid;
using ZeroAlloc.Results;

namespace Minos.Caching;

/// <summary>A stage that answers a repeated request from a <see cref="HybridCache"/>, and stores each successful answer.</summary>
/// <remarks>
/// <para>
/// The key is a SHA-256 digest over the definition, the state, the images and the model, and the provider and endpoint of
/// the inner client's <see cref="DecisionClientMetadata"/>, so clients for different providers never share an answer. The
/// retry attempt is not part of it.
/// </para>
/// <para>
/// A hit returns the stored response, with <see cref="DecisionResponse.IsCached"/> set and the model, usage and cost of the
/// call that first produced it. An in-process hit returns the stored instance. Only a success is stored: an error or a
/// refusal passes through unchanged. Concurrent identical misses each call the provider; there is no stampede protection,
/// since HybridCache's would cache failures too.
/// </para>
/// <para>
/// The cache must have the decision serializer: <c>services.AddHybridCache().AddDecisionSerializer()</c>. Disposing the
/// stage disposes the inner client, never the cache.
/// </para>
/// </remarks>
public sealed class CachingDecisionClient : DelegatingDecisionClient
{
    private const string UnknownProvider = "unknown";

    // A read that writes nothing: the factory never runs, a miss returns null, and neither layer is written.
    private static readonly HybridCacheEntryOptions Lookup = new()
    {
        Flags = HybridCacheEntryFlags.DisableUnderlyingData
            | HybridCacheEntryFlags.DisableLocalCacheWrite
            | HybridCacheEntryFlags.DisableDistributedCacheWrite,
    };

    private readonly HybridCache _cache;
    private readonly string _provider;
    private readonly string? _endpoint;
    private readonly string _keyPrefix;
    private readonly HybridCacheEntryOptions _store;
    private readonly HybridCacheEntryOptions _promote;

    /// <summary>Initializes a new instance of the <see cref="CachingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client whose answers are cached.</param>
    /// <param name="cache">The cache, with the decision serializer registered.</param>
    /// <param name="options">The expiry and key prefix; <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="cache"/> is <see langword="null"/>.</exception>
    public CachingDecisionClient(IDecisionClient innerClient, HybridCache cache, DecisionCacheOptions? options = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(cache);
        options ??= new DecisionCacheOptions();
        _cache = cache;
        var metadata = innerClient.GetService<DecisionClientMetadata>();
        _provider = metadata?.ProviderName ?? UnknownProvider;
        _endpoint = metadata?.Endpoint?.AbsoluteUri;
        _keyPrefix = options.KeyPrefix;
        _store = new HybridCacheEntryOptions { Expiration = options.Expiration, LocalCacheExpiration = options.LocalCacheExpiration };
        _promote = new HybridCacheEntryOptions
        {
            Expiration = options.Expiration,
            LocalCacheExpiration = options.LocalCacheExpiration,
            Flags = HybridCacheEntryFlags.DisableDistributedCacheWrite,
        };
    }

    /// <inheritdoc />
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = DecisionCacheKey.Create(request, _provider, _endpoint, _keyPrefix);
        var lookup = _cache.GetOrCreateAsync<object?, CachedDecision?>(
            key, null, static (_, _) => default, Lookup, null, cancellationToken);
        return lookup.IsCompletedSuccessfully
            ? Answer(lookup.Result, request, key, cancellationToken)
            : AwaitLookupAsync(lookup, request, key, cancellationToken);
    }

    private async ValueTask<Result<DecisionResponse, DecisionError>> AwaitLookupAsync(
        ValueTask<CachedDecision?> lookup, DecisionRequest request, string key, CancellationToken cancellationToken)
        => await Answer(await lookup.ConfigureAwait(false), request, key, cancellationToken).ConfigureAwait(false);

    private ValueTask<Result<DecisionResponse, DecisionError>> Answer(
        CachedDecision? entry, DecisionRequest request, string key, CancellationToken cancellationToken)
    {
        if (entry is null || entry.ResponseFor(request.Definition, out var firstBuild) is not { } hit)
        {
            return MissAsync(request, key, cancellationToken);
        }

        return firstBuild ? PromoteAsync(entry, hit, key) : new(Result<DecisionResponse, DecisionError>.Success(hit));
    }

    // A response rebuilt from the distributed cache is kept in the in-process one, so the next hit reuses it.
    private async ValueTask<Result<DecisionResponse, DecisionError>> PromoteAsync(CachedDecision entry, DecisionResponse hit, string key)
    {
        await _cache.SetAsync(key, entry, _promote, null, CancellationToken.None).ConfigureAwait(false);
        return Result<DecisionResponse, DecisionError>.Success(hit);
    }

    // The answer is paid for once it arrives, so it is stored even when the caller cancels afterwards.
    private async ValueTask<Result<DecisionResponse, DecisionError>> MissAsync(DecisionRequest request, string key, CancellationToken cancellationToken)
    {
        var result = await InnerClient.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await _cache.SetAsync(key, CachedDecision.From(result.Value.AsCached()), _store, null, CancellationToken.None).ConfigureAwait(false);
        }

        return result;
    }
}
