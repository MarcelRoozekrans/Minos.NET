using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Minos.Caching.Tests;

public sealed class UseCachingTests
{
    [Fact]
    public async Task Takes_the_in_process_cache_from_the_builds_services()
    {
        using var services = Responses.Services();
        var inner = new ScriptedClient();
        using var client = inner.AsBuilder().UseCaching().Build(services);

        await client.EvaluateAsync(Responses.Request());
        var hit = await client.EvaluateAsync(Responses.Request());

        Assert.True(hit.Value.IsCached);
        Assert.Equal(1, inner.Calls);
        Assert.NotNull(client.GetService(typeof(CachingDecisionClient)));
    }

    [Fact]
    public async Task Takes_both_caches_and_the_logger_from_the_builds_services()
    {
        var distributed = new FailingDistributedCache();
        var collector = new FakeLogCollector();
        using var logs = new FakeLoggerProvider(collector);
        using var services = Responses.Services(distributed, logs);
        var inner = new ScriptedClient();
        using var client = inner.AsBuilder().UseCaching().Build(services);

        await client.EvaluateAsync(Responses.Request());
        var hit = await client.EvaluateAsync(Responses.Request());

        Assert.True(hit.Value.IsCached);
        Assert.Equal(1, inner.Calls);
        // The read and the write reached the registered distributed cache, and both failures were logged in the
        // client's category.
        Assert.Equal(2, distributed.Calls);
        var records = collector.GetSnapshot();
        Assert.Equal([1007, 1008], records.Select(record => record.Id.Id));
        Assert.All(records, record => Assert.Equal("Minos.DecisionClient", record.Category));
    }

    [Fact]
    public async Task Works_with_an_in_process_cache_and_an_in_memory_distributed_cache_from_the_services()
    {
        var collection = new ServiceCollection();
        collection.AddMemoryCache();
        collection.AddDistributedMemoryCache();
        using var services = collection.BuildServiceProvider();
        var inner = new ScriptedClient();
        using var client = inner.AsBuilder().UseCaching().Build(services);

        await client.EvaluateAsync(Responses.Request());
        var hit = await client.EvaluateAsync(Responses.Request());

        Assert.True(hit.Value.IsCached);
        Assert.NotNull(services.GetRequiredService<IDistributedCache>().Get(DecisionCacheKey.Create(Responses.Request(), "unknown", null, null, "minos:")));
    }

    [Fact]
    public async Task Uses_caches_passed_to_it_and_applies_the_options()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        var options = default(DecisionCacheOptions);
        using var client = new ScriptedClient().AsBuilder()
            .UseCaching(memory, distributed, o => { o.KeyPrefix = "app1:"; options = o; })
            .Build();

        var result = await client.EvaluateAsync(Responses.Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("app1:", options!.KeyPrefix);
        // No metadata, so the provider is "unknown" and there is no endpoint.
        var key = DecisionCacheKey.Create(Responses.Request(), "unknown", null, null, "app1:");
        Assert.True(distributed.Writes.ContainsKey(key));
        Assert.True(memory.TryGetValue(key, out _));
    }

    [Fact]
    public async Task Caches_passed_to_it_win_over_the_ones_in_the_services()
    {
        var registered = new CountingDistributedCache();
        using var services = Responses.Services(registered);
        using var passedMemory = Responses.Memory();
        var passed = new CountingDistributedCache();
        using var client = new ScriptedClient().AsBuilder().UseCaching(passedMemory, passed).Build(services);

        await client.EvaluateAsync(Responses.Request());

        var key = DecisionCacheKey.Create(Responses.Request(), "unknown", null, null, "minos:");
        Assert.True(passedMemory.TryGetValue(key, out _));
        Assert.False(services.GetRequiredService<IMemoryCache>().TryGetValue(key, out _));
        Assert.NotNull(passed.Peek(key));
        Assert.Null(registered.Peek(key));
        Assert.Equal(0, registered.ReadCount + registered.WriteCount);
    }

    [Fact]
    public void Throws_at_build_when_no_in_process_cache_is_registered()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => new ScriptedClient().AsBuilder().UseCaching().Build(services));

        Assert.Equal(
            "UseCaching found no IMemoryCache: register one with services.AddMemoryCache() and pass the services to Build, or pass one to UseCaching.",
            exception.Message);
    }

    [Fact]
    public void Throws_at_build_when_an_option_is_invalid()
    {
        using var services = Responses.Services();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ScriptedClient().AsBuilder().UseCaching(configure: o => o.Expiration = TimeSpan.Zero).Build(services));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ScriptedClient().AsBuilder().UseCaching(configure: o => o.LocalCacheExpiration = TimeSpan.FromSeconds(-1)).Build(services));
        Assert.Throws<ArgumentNullException>(
            () => new ScriptedClient().AsBuilder().UseCaching(configure: o => o.KeyPrefix = null!).Build(services));
    }

    [Fact]
    public void Refuses_null()
    {
        Assert.Throws<ArgumentNullException>(() => CachingDecisionClientBuilderExtensions.UseCaching(null!));
    }
}
