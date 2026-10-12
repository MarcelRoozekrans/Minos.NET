using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Minos.Caching.Tests;

/// <summary>
/// An in-memory distributed cache that several HybridCache instances share as their L2, and that records the entry options
/// of each write.
/// </summary>
/// <remarks>
/// HybridCache ignores a <see cref="MemoryDistributedCache"/> registered beside its own <see cref="MemoryCache"/>, as a
/// second in-process copy, so a test that needs a real L2 wraps it in a type of its own.
/// </remarks>
internal sealed class SharedDistributedCache : IDistributedCache
{
    private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
    private int _writeCount;

    public ConcurrentDictionary<string, DistributedCacheEntryOptions> Writes { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets how many writes reached this cache, a rewrite of a key included.</summary>
    public int WriteCount => Volatile.Read(ref _writeCount);

    public byte[]? Get(string key) => _inner.Get(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => _inner.GetAsync(key, token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        Writes[key] = options;
        Interlocked.Increment(ref _writeCount);
        _inner.Set(key, value, options);
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        Writes[key] = options;
        Interlocked.Increment(ref _writeCount);
        return _inner.SetAsync(key, value, options, token);
    }

    public void Refresh(string key) => _inner.Refresh(key);

    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);

    public void Remove(string key) => _inner.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default) => _inner.RemoveAsync(key, token);
}
