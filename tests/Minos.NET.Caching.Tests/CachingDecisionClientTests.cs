using Microsoft.Extensions.Caching.Distributed;

namespace Minos.Caching.Tests;

public sealed class CachingDecisionClientTests
{
    private static readonly DecisionClientMetadata TypeSafe = new("typesafe", new Uri("https://api.typesafe.ai/"), "jev-latest");

    [Fact]
    public async Task A_miss_calls_the_provider_and_returns_its_response_uncached()
    {
        using var services = Responses.Services();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, services.Cache());

        var result = await stage.EvaluateAsync(Responses.Request());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsCached);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_repeat_is_an_L1_hit_returning_one_stored_instance()
    {
        using var services = Responses.Services();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, services.Cache());
        var miss = await stage.EvaluateAsync(Responses.Request());

        var first = await stage.EvaluateAsync(Responses.Request());
        var second = await stage.EvaluateAsync(Responses.Request() with { RetryAttempt = 1 });

        Assert.Equal(1, inner.Calls);
        Assert.True(first.Value.IsCached);
        Assert.Same(first.Value, second.Value);
        Assert.NotSame(miss.Value, first.Value);
        Assert.Same(Responses.Triage, first.Value.Definition);
        Assert.Equal("jev-1.13.0", first.Value.Model);
        Assert.Equal(miss.Value.Usage, first.Value.Usage);
        Assert.Equal(0.7, first.Value.Answers[1].Confidence);
    }

    [Fact]
    public async Task An_L1_hit_completes_synchronously()
    {
        using var services = Responses.Services();
        using var stage = new CachingDecisionClient(new ScriptedClient(TypeSafe), services.Cache());
        await stage.EvaluateAsync(Responses.Request());

        var hit = stage.EvaluateAsync(Responses.Request());

        Assert.True(hit.IsCompletedSuccessfully);
        Assert.True((await hit).Value.IsCached);
    }

    [Fact]
    public async Task A_different_state_or_model_is_a_miss()
    {
        using var services = Responses.Services();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, services.Cache());
        await stage.EvaluateAsync(Responses.Request());

        await stage.EvaluateAsync(Responses.Request("I was charged once."));
        await stage.EvaluateAsync(Responses.Request() with { Model = "jev-1.13.0" });

        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Two_clients_for_different_endpoints_never_share_an_answer()
    {
        using var services = Responses.Services();
        var typeSafe = new ScriptedClient(TypeSafe);
        // The same provider at another endpoint, so only the endpoint tells the keys apart.
        var openRouter = new ScriptedClient(new DecisionClientMetadata("typesafe", new Uri("https://eu.api.typesafe.ai/"), "jev-latest"));
        using var first = new CachingDecisionClient(typeSafe, services.Cache());
        using var second = new CachingDecisionClient(openRouter, services.Cache());

        await first.EvaluateAsync(Responses.Request());
        var result = await second.EvaluateAsync(Responses.Request());

        Assert.False(result.Value.IsCached);
        Assert.Equal(1, openRouter.Calls);
    }

    [Theory]
    [InlineData(DecisionErrorKind.Server)]
    [InlineData(DecisionErrorKind.RateLimited)]
    [InlineData(DecisionErrorKind.Refused)]
    [InlineData(DecisionErrorKind.Validation)]
    public async Task A_failure_passes_through_and_is_never_stored(DecisionErrorKind kind)
    {
        var shared = new SharedDistributedCache();
        using var services = Responses.Services(shared);
        var inner = new ScriptedClient(TypeSafe).ThenFail(kind).ThenAnswer();
        using var stage = new CachingDecisionClient(inner, services.Cache());

        var failed = await stage.EvaluateAsync(Responses.Request());
        Assert.Equal(0, shared.WriteCount);
        var retried = await stage.EvaluateAsync(Responses.Request());

        Assert.Equal(kind, failed.Error.Kind);
        Assert.True(retried.IsSuccess);
        Assert.False(retried.Value.IsCached);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task An_L2_hit_with_an_empty_L1_is_rebuilt_against_the_requests_definition()
    {
        var shared = new SharedDistributedCache();
        using var writerServices = Responses.Services(shared);
        using var readerServices = Responses.Services(shared);
        var writerInner = new ScriptedClient(TypeSafe);
        var readerInner = new ScriptedClient(TypeSafe);
        using var writer = new CachingDecisionClient(writerInner, writerServices.Cache());
        using var reader = new CachingDecisionClient(readerInner, readerServices.Cache());
        var stored = await writer.EvaluateAsync(Responses.Request());
        Assert.Equal(1, shared.WriteCount);

        var hit = await reader.EvaluateAsync(Responses.Request());
        var again = await reader.EvaluateAsync(Responses.Request());

        Assert.Equal(0, readerInner.Calls);
        // The promote wrote L1 only: the reader wrote nothing to L2.
        Assert.Equal(1, shared.WriteCount);
        Assert.Equal("gen-0001", hit.Value.Id);
        Assert.Equal("Together", hit.Value.Provider);
        Assert.True(hit.Value.IsCached);
        Assert.Same(Responses.Triage, hit.Value.Definition);
        Assert.Equal(stored.Value.Model, hit.Value.Model);
        Assert.Equal(stored.Value.Usage, hit.Value.Usage);
        Assert.Equal(stored.Value.Id, hit.Value.Id);
        Assert.Equal(stored.Value.Provider, hit.Value.Provider);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(stored.Value.Answers[i].Value, hit.Value.Answers[i].Value);
            Assert.Equal(stored.Value.Answers[i].ChosenIndex, hit.Value.Answers[i].ChosenIndex);
            Assert.Equal(stored.Value.Answers[i].Confidence, hit.Value.Answers[i].Confidence);
            Assert.Equal(stored.Value.Answers[i].Probabilities.ToArray(), hit.Value.Answers[i].Probabilities.ToArray());
        }

        // The rebuilt entry was promoted to L1, so the second read returns the same instance.
        Assert.Same(hit.Value, again.Value);
    }

    [Fact]
    public async Task An_equal_definition_in_another_instance_gets_a_response_for_its_own_instance()
    {
        using var services = Responses.Services();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, services.Cache());
        await stage.EvaluateAsync(Responses.Request());
        var copy = new QuestionSetDefinition([.. Responses.Triage.Questions]);

        var hit = await stage.EvaluateAsync(new DecisionRequest(copy, "I was charged twice."));

        Assert.Equal(1, inner.Calls);
        Assert.True(hit.Value.IsCached);
        Assert.Same(copy, hit.Value.Definition);
    }

    [Fact]
    public async Task An_unreadable_L2_entry_is_a_miss_and_is_overwritten()
    {
        var shared = new SharedDistributedCache();
        using var services = Responses.Services(shared);
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, services.Cache());
        var key = DecisionCacheKey.Create(Responses.Request(), "typesafe", "https://api.typesafe.ai/", "minos:");

        // HybridCache frames every L2 value with a header that names the key, so it rejects these raw bytes before the
        // decision serializer sees them. The test still proves that an unreadable entry is a miss the next success
        // overwrites. The next test, and CachedDecisionSerializerTests, cover the bytes the serializer itself cannot read.
        await shared.SetAsync(key, "not json"u8.ToArray(), new DistributedCacheEntryOptions());

        var first = await stage.EvaluateAsync(Responses.Request());
        using var otherServices = Responses.Services(shared);
        using var other = new CachingDecisionClient(new ScriptedClient(TypeSafe), otherServices.Cache());
        var second = await other.EvaluateAsync(Responses.Request());

        Assert.True(first.IsSuccess);
        Assert.False(first.Value.IsCached);
        Assert.Equal(1, inner.Calls);
        Assert.True(second.Value.IsCached);
    }

    [Fact]
    public async Task An_L2_entry_the_serializer_cannot_read_is_a_miss_and_is_overwritten()
    {
        var shared = new SharedDistributedCache();
        var key = DecisionCacheKey.Create(Responses.Request(), "typesafe", "https://api.typesafe.ai/", "minos:");
        using (var foreignServices = Responses.Services(shared))
        {
            // A string stored through another HybridCache: a valid frame around bytes that are no decision entry.
            await foreignServices.Cache().SetAsync(key, "not json");
        }

        using var services = Responses.Services(shared);
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, services.Cache());
        var first = await stage.EvaluateAsync(Responses.Request());
        using var otherServices = Responses.Services(shared);
        using var other = new CachingDecisionClient(new ScriptedClient(TypeSafe), otherServices.Cache());
        var second = await other.EvaluateAsync(Responses.Request());

        Assert.True(first.IsSuccess);
        Assert.False(first.Value.IsCached);
        Assert.Equal(1, inner.Calls);
        Assert.True(second.Value.IsCached);
    }

    [Fact]
    public async Task The_key_prefix_and_expiry_options_apply()
    {
        var shared = new SharedDistributedCache();
        using var services = Responses.Services(shared);
        using var stage = new CachingDecisionClient(
            new ScriptedClient(TypeSafe), services.Cache(), new DecisionCacheOptions { KeyPrefix = "app1:", Expiration = TimeSpan.FromMinutes(5) });

        await stage.EvaluateAsync(Responses.Request());

        var key = DecisionCacheKey.Create(Responses.Request(), "typesafe", "https://api.typesafe.ai/", "app1:");
        var unprefixed = DecisionCacheKey.Create(Responses.Request(), "typesafe", "https://api.typesafe.ai/", "minos:");
        Assert.NotNull(await shared.GetAsync(key));
        Assert.Null(await shared.GetAsync(unprefixed));
        Assert.Equal(TimeSpan.FromMinutes(5), shared.Writes[key].AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task Without_metadata_the_provider_is_unknown_and_the_cache_still_works()
    {
        using var services = Responses.Services();
        var inner = new ScriptedClient();
        using var stage = new CachingDecisionClient(inner, services.Cache());

        await stage.EvaluateAsync(Responses.Request());
        var hit = await stage.EvaluateAsync(Responses.Request());

        Assert.True(hit.Value.IsCached);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public void The_constructor_refuses_null()
    {
        using var services = Responses.Services();

        Assert.Throws<ArgumentNullException>(() => new CachingDecisionClient(new ScriptedClient(), null!));
        Assert.Throws<ArgumentNullException>(() => new CachingDecisionClient(null!, services.Cache()));
    }
}
