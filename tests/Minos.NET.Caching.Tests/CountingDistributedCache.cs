using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Minos.Caching.Tests;

/// <summary>
/// A <see cref="MemoryDistributedCache"/> that counts its reads and writes and records the entry options of each write,
/// so a test can tell which level answered.
/// </summary>
internal sealed class CountingDistributedCache : IDistributedCache
{
    private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
    private int _readCount;
    private int _writeCount;

    public ConcurrentDictionary<string, DistributedCacheEntryOptions> Writes { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets how many reads reached this cache.</summary>
    public int ReadCount => Volatile.Read(ref _readCount);

    /// <summary>Gets how many writes reached this cache, a rewrite of a key included.</summary>
    public int WriteCount => Volatile.Read(ref _writeCount);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/> without counting it, as another app would.</summary>
    public void Seed(string key, byte[] value) => _inner.Set(key, value, new DistributedCacheEntryOptions());

    /// <summary>Reads <paramref name="key"/> without counting it.</summary>
    public byte[]? Peek(string key) => _inner.Get(key);

    public byte[]? Get(string key)
    {
        Interlocked.Increment(ref _readCount);
        return _inner.Get(key);
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        Interlocked.Increment(ref _readCount);
        return _inner.GetAsync(key, token);
    }

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
