using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Minos.Caching.Tests;

public sealed class UseHybridCacheTests
{
    [Fact]
    public async Task Takes_the_cache_from_the_builds_services()
    {
        using var services = Responses.Services();
        var inner = new ScriptedClient();
        using var client = inner.AsBuilder().UseHybridCache().Build(services);

        await client.EvaluateAsync(Responses.Request());
        var hit = await client.EvaluateAsync(Responses.Request());

        Assert.True(hit.Value.IsCached);
        Assert.NotNull(client.GetService(typeof(CachingDecisionClient)));
    }

    [Fact]
    public async Task Uses_a_cache_passed_to_it_and_applies_the_options()
    {
        using var services = Responses.Services();
        var options = default(DecisionCacheOptions);
        using var client = new ScriptedClient().AsBuilder()
            .UseHybridCache(services.Cache(), o => { o.KeyPrefix = "app1:"; options = o; })
            .Build();

        var result = await client.EvaluateAsync(Responses.Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("app1:", options!.KeyPrefix);
    }

    [Fact]
    public void Throws_at_build_when_no_cache_is_registered()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => new ScriptedClient().AsBuilder().UseHybridCache().Build(services));

        Assert.Contains("AddHybridCache().AddDecisionSerializer()", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Throws_at_build_when_the_serializer_is_not_registered()
    {
        var collection = new ServiceCollection();
        collection.AddHybridCache();
        using var services = collection.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => new ScriptedClient().AsBuilder().UseHybridCache().Build(services));

        Assert.Contains("AddDecisionSerializer()", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddDecisionSerializer_registers_the_entry_serializer()
    {
        using var services = Responses.Services();

        Assert.IsType<CachedDecisionSerializer>(services.GetService<IHybridCacheSerializer<CachedDecision>>());
    }

    [Fact]
    public void Refuses_null()
    {
        Assert.Throws<ArgumentNullException>(() => CachingDecisionClientBuilderExtensions.UseHybridCache(null!));
        Assert.Throws<ArgumentNullException>(() => DecisionCacheHybridCacheBuilderExtensions.AddDecisionSerializer(null!));
    }
}
