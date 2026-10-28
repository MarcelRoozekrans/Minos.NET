using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Minos.Caching;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>
/// A stage that answers a repeated request from an in-process <see cref="IMemoryCache"/>, then from an optional
/// <see cref="IDistributedCache"/>, and stores each successful answer in both.
/// </summary>
/// <remarks>
/// <para>
/// The key is a SHA-256 digest over the definition, the state, the images, and the provider, endpoint and model of the
/// inner client's <see cref="DecisionClientMetadata"/>, so clients for different providers or models never share an
/// answer. The model is the request's own, or the client's default model when the request names none. When the metadata
/// has a <see cref="DecisionClientMetadata.CacheScope"/>, it is part of the key too, so clients with different scopes
/// never share an answer either. The retry attempt is not part of it. Both levels use the same key.
/// </para>
/// <para>
/// A hit returns the stored response, with <see cref="DecisionResponse.IsCached"/> set and the model, usage and cost of the
/// call that first produced it. An in-process hit returns the stored instance and completes synchronously. A distributed
/// hit is rebuilt against the request's definition and kept in process. Only a success is stored: an error or a refusal
/// passes through unchanged. Concurrent identical misses each call the provider; there is no stampede protection. A miss
/// awaits the distributed write, which is not cancelled with the call, before it returns, so a slow distributed cache
/// delays each miss.
/// </para>
/// <para>
/// A distributed cache that throws never fails a call: a failed read is a miss, logged as event 1007, and a failed write
/// leaves the answer out of the distributed cache, logged as event 1008. Each in-process entry has a size of 1, so an
/// <see cref="IMemoryCache"/> with a size limit counts entries. Disposing the stage disposes the inner client, never a
/// cache.
/// </para>
/// </remarks>
public sealed class CachingDecisionClient : DelegatingDecisionClient
{
    private const string UnknownProvider = "unknown";

    private readonly IMemoryCache _memory;
    private readonly IDistributedCache? _distributed;
    private readonly ILogger? _logger;
    private readonly string _provider;
    private readonly string? _endpoint;
    private readonly string? _defaultModel;
    private readonly string? _scope;
    private readonly string _keyPrefix;
    private readonly MemoryCacheEntryOptions _localOptions;
    private readonly DistributedCacheEntryOptions _distributedOptions;
    private volatile bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="CachingDecisionClient"/> class.</summary>
    /// <param name="innerClient">The client whose answers are cached.</param>
    /// <param name="memoryCache">The in-process cache.</param>
    /// <param name="distributedCache">The distributed cache, such as Redis; <see langword="null"/> to cache in process only.</param>
    /// <param name="options">The expiry and key prefix; <see langword="null"/> for the defaults.</param>
    /// <param name="logger">Receives events 1007 and 1008 when the distributed cache fails; <see langword="null"/> to log nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="innerClient"/> or <paramref name="memoryCache"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="distributedCache"/> is set and <paramref name="options"/> has <see cref="DecisionCacheOptions.UseDistributedCache"/> set to <see langword="false"/>.</exception>
    public CachingDecisionClient(
        IDecisionClient innerClient,
        IMemoryCache memoryCache,
        IDistributedCache? distributedCache = null,
        DecisionCacheOptions? options = null,
        ILogger? logger = null)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(memoryCache);
        options ??= new DecisionCacheOptions();
        if (!options.UseDistributedCache && distributedCache is not null)
        {
            throw new ArgumentException(
                "A distributed cache was passed with DecisionCacheOptions.UseDistributedCache set to false.", nameof(distributedCache));
        }

        _memory = memoryCache;
        _distributed = distributedCache;
        _logger = logger;
        var metadata = innerClient.GetService<DecisionClientMetadata>();
        _provider = metadata?.ProviderName ?? UnknownProvider;
        // The metadata takes any Uri: a relative one, such as a client that reports only its path, is keyed by its text.
        _endpoint = metadata?.Endpoint is { } endpoint
            ? endpoint.IsAbsoluteUri ? endpoint.AbsoluteUri : endpoint.OriginalString
            : null;
        _defaultModel = metadata?.DefaultModel;
        _scope = metadata?.CacheScope;
        _keyPrefix = options.KeyPrefix;
        _localOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = options.LocalCacheExpiration ?? options.Expiration,
            Size = 1,
        };
        _distributedOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = options.Expiration };
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The stage is disposed.</exception>
    public override ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = DecisionCacheKey.Create(request, _provider, _endpoint, _defaultModel, _scope, _keyPrefix);
        if (_memory.TryGetValue(key, out var stored) && stored is CachedDecision entry
            && entry.ResponseFor(request.Definition) is { } hit)
        {
            return new(Result<DecisionResponse, DecisionError>.Success(hit));
        }

        return _distributed is null ? MissAsync(request, key, cancellationToken) : ReadDistributedAsync(request, key, cancellationToken);
    }

    // The in-process cache missed: try the distributed one, and keep a hit from it in process.
    private async ValueTask<Result<DecisionResponse, DecisionError>> ReadDistributedAsync(
        DecisionRequest request, string key, CancellationToken cancellationToken)
    {
        byte[]? bytes;
        try
        {
            bytes = await _distributed!.GetAsync(key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            if (_logger is not null)
            {
                DecisionLog.CacheReadFailed(_logger, _provider, exception);
            }

            bytes = null;
        }

        if (bytes is not null)
        {
            var entry = CachedDecisionFormat.Read(bytes);
            if (entry.ResponseFor(request.Definition) is { } hit)
            {
                _memory.Set(key, entry, _localOptions);
                return Result<DecisionResponse, DecisionError>.Success(hit);
            }
        }

        return await MissAsync(request, key, cancellationToken).ConfigureAwait(false);
    }

    // The answer is paid for once it arrives, so it is stored even when the caller cancels afterwards.
    private async ValueTask<Result<DecisionResponse, DecisionError>> MissAsync(DecisionRequest request, string key, CancellationToken cancellationToken)
    {
        var result = await InnerClient.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            var entry = CachedDecision.From(result.Value.AsCached());
            _memory.Set(key, entry, _localOptions);
            if (_distributed is not null)
            {
                await WriteDistributedAsync(key, entry).ConfigureAwait(false);
            }
        }

        return result;
    }

    private async ValueTask WriteDistributedAsync(string key, CachedDecision entry)
    {
        try
        {
            await _distributed!.SetAsync(key, CachedDecisionFormat.Write(entry), _distributedOptions, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (_logger is not null)
            {
                DecisionLog.CacheWriteFailed(_logger, _provider, exception);
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
