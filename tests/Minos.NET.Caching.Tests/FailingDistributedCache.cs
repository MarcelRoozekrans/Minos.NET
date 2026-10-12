using Microsoft.Extensions.Caching.Distributed;

namespace Minos.Caching.Tests;

/// <summary>A distributed cache that is down: every call throws, and is counted.</summary>
internal sealed class FailingDistributedCache : IDistributedCache
{
    private int _calls;

    /// <summary>Gets how many calls reached this cache.</summary>
    public int Calls => Volatile.Read(ref _calls);

    public byte[]? Get(string key) => throw Fail();

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Fail();

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Fail();

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Fail();

    public void Refresh(string key) => throw Fail();

    public Task RefreshAsync(string key, CancellationToken token = default) => throw Fail();

    public void Remove(string key) => throw Fail();

    public Task RemoveAsync(string key, CancellationToken token = default) => throw Fail();

    private InvalidOperationException Fail()
    {
        Interlocked.Increment(ref _calls);
        return new InvalidOperationException("The distributed cache is down.");
    }
}
