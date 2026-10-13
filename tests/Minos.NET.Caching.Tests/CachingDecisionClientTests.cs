using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Minos.Caching.Tests;

public sealed class CachingDecisionClientTests
{
    private static readonly DecisionClientMetadata TypeSafe = new("typesafe", new Uri("https://api.typesafe.ai/"), "jev-latest");

    private static string Key(string prefix = "minos:")
        => DecisionCacheKey.Create(Responses.Request(), "typesafe", "https://api.typesafe.ai/", "jev-latest", prefix);

    [Fact]
    public void The_constructor_rejects_a_distributed_cache_with_UseDistributedCache_off()
    {
        using var memory = Responses.Memory();
        var options = new DecisionCacheOptions { UseDistributedCache = false };

        var error = Assert.Throws<ArgumentException>(
            () => new CachingDecisionClient(new ScriptedClient(), memory, new CountingDistributedCache(), options));

        Assert.Equal("distributedCache", error.ParamName);
    }

    [Fact]
    public async Task A_miss_calls_the_provider_and_returns_its_response_uncached()
    {
        using var memory = Responses.Memory();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory);

        var result = await stage.EvaluateAsync(Responses.Request());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsCached);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_repeat_is_an_L1_hit_returning_one_stored_instance()
    {
        using var memory = Responses.Memory();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory);
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
    public async Task An_L1_hit_completes_synchronously_and_reads_no_L2()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        using var stage = new CachingDecisionClient(new ScriptedClient(TypeSafe), memory, distributed);
        await stage.EvaluateAsync(Responses.Request());
        var reads = distributed.ReadCount;

        var hit = stage.EvaluateAsync(Responses.Request());

        Assert.True(hit.IsCompletedSuccessfully);
        Assert.True((await hit).Value.IsCached);
        Assert.Equal(reads, distributed.ReadCount);
    }

    [Fact]
    public async Task A_different_state_or_model_is_a_miss()
    {
        using var memory = Responses.Memory();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory);
        await stage.EvaluateAsync(Responses.Request());

        await stage.EvaluateAsync(Responses.Request("I was charged once."));
        await stage.EvaluateAsync(Responses.Request() with { Model = "jev-1.13.0" });

        Assert.Equal(3, inner.Calls);
    }

    [Fact]
    public async Task Two_clients_for_different_endpoints_never_share_an_answer()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        var typeSafe = new ScriptedClient(TypeSafe);
        // The same provider at another endpoint, so only the endpoint tells the keys apart.
        var eu = new ScriptedClient(new DecisionClientMetadata("typesafe", new Uri("https://eu.api.typesafe.ai/"), "jev-latest"));
        using var first = new CachingDecisionClient(typeSafe, memory, distributed);
        using var second = new CachingDecisionClient(eu, memory, distributed);

        await first.EvaluateAsync(Responses.Request());
        var result = await second.EvaluateAsync(Responses.Request());

        Assert.False(result.Value.IsCached);
        Assert.Equal(1, eu.Calls);
    }

    [Fact]
    public async Task Two_clients_on_one_host_with_different_paths_never_share_an_answer()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        // Two System One clients on one base address that post to different paths, as SystemOneClient reports them.
        var first = new ScriptedClient(new DecisionClientMetadata("custom", new Uri("http://localhost:8080/a/v1/systemone"), "m"));
        var second = new ScriptedClient(new DecisionClientMetadata("custom", new Uri("http://localhost:8080/b/v1/systemone"), "m"));
        using var a = new CachingDecisionClient(first, memory, distributed);
        using var b = new CachingDecisionClient(second, memory, distributed);

        await a.EvaluateAsync(Responses.Request());
        var result = await b.EvaluateAsync(Responses.Request());

        Assert.False(result.Value.IsCached);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task Two_clients_that_differ_only_in_their_default_model_never_share_an_answer()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        var latest = new ScriptedClient(TypeSafe);
        // The same provider and endpoint, so only the default model tells the keys apart.
        var pinned = new ScriptedClient(TypeSafe with { DefaultModel = "jev-1.13.0" });
        using var first = new CachingDecisionClient(latest, memory, distributed);
        using var second = new CachingDecisionClient(pinned, memory, distributed);

        await first.EvaluateAsync(Responses.Request());
        var result = await second.EvaluateAsync(Responses.Request());

        Assert.False(result.Value.IsCached);
        Assert.Equal(1, pinned.Calls);
    }

    [Fact]
    public async Task A_request_naming_the_model_another_client_defaults_to_shares_its_answer()
    {
        using var memory = Responses.Memory();
        var latest = new ScriptedClient(TypeSafe);
        var pinned = new ScriptedClient(TypeSafe with { DefaultModel = "jev-1.13.0" });
        using var first = new CachingDecisionClient(latest, memory);
        using var second = new CachingDecisionClient(pinned, memory);

        await first.EvaluateAsync(Responses.Request());
        var result = await second.EvaluateAsync(Responses.Request() with { Model = "jev-latest" });

        Assert.True(result.Value.IsCached);
        Assert.Equal(0, pinned.Calls);
    }

    [Theory]
    [InlineData(DecisionErrorKind.Server)]
    [InlineData(DecisionErrorKind.RateLimited)]
    [InlineData(DecisionErrorKind.Refused)]
    [InlineData(DecisionErrorKind.Validation)]
    public async Task A_failure_passes_through_and_is_never_stored(DecisionErrorKind kind)
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        var inner = new ScriptedClient(TypeSafe).ThenFail(kind).ThenAnswer();
        using var stage = new CachingDecisionClient(inner, memory, distributed);

        var failed = await stage.EvaluateAsync(Responses.Request());
        Assert.Equal(0, distributed.WriteCount);
        Assert.Equal(0, memory.Count);
        var retried = await stage.EvaluateAsync(Responses.Request());

        Assert.Equal(kind, failed.Error.Kind);
        Assert.True(retried.IsSuccess);
        Assert.False(retried.Value.IsCached);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task A_success_is_stored_in_both_levels()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        using var stage = new CachingDecisionClient(new ScriptedClient(TypeSafe), memory, distributed);

        await stage.EvaluateAsync(Responses.Request());

        Assert.IsType<CachedDecision>(memory.Get(Key()));
        Assert.Equal(1, distributed.WriteCount);
        Assert.NotNull(distributed.Peek(Key()));
    }

    [Fact]
    public async Task An_L2_hit_with_an_empty_L1_is_rebuilt_against_the_requests_definition_and_promoted()
    {
        var distributed = new CountingDistributedCache();
        using var writerMemory = Responses.Memory();
        using var readerMemory = Responses.Memory();
        var writerInner = new ScriptedClient(TypeSafe);
        var readerInner = new ScriptedClient(TypeSafe);
        using var writer = new CachingDecisionClient(writerInner, writerMemory, distributed);
        using var reader = new CachingDecisionClient(readerInner, readerMemory, distributed);
        var stored = await writer.EvaluateAsync(Responses.Request());
        Assert.Equal(1, distributed.WriteCount);
        var readsBefore = distributed.ReadCount;

        var hit = await reader.EvaluateAsync(Responses.Request());
        Assert.Equal(readsBefore + 1, distributed.ReadCount);
        var again = await reader.EvaluateAsync(Responses.Request());

        Assert.Equal(0, readerInner.Calls);
        // The promote wrote L1 only: the reader wrote nothing to L2, and its second read never reached L2.
        Assert.Equal(1, distributed.WriteCount);
        Assert.Equal(readsBefore + 1, distributed.ReadCount);
        Assert.Equal("gen-0001", hit.Value.Id);
        Assert.Equal("Together", hit.Value.UpstreamProvider);
        Assert.True(hit.Value.IsCached);
        Assert.Same(Responses.Triage, hit.Value.Definition);
        Assert.Equal(stored.Value.Model, hit.Value.Model);
        Assert.Equal(stored.Value.Usage, hit.Value.Usage);
        Assert.Equal(stored.Value.Id, hit.Value.Id);
        Assert.Equal(stored.Value.UpstreamProvider, hit.Value.UpstreamProvider);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(stored.Value.Answers[i].Probability, hit.Value.Answers[i].Probability);
            Assert.Equal(stored.Value.Answers[i].Expected, hit.Value.Answers[i].Expected);
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
        using var memory = Responses.Memory();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory);
        await stage.EvaluateAsync(Responses.Request());
        var copy = new QuestionSetDefinition([.. Responses.Triage.Questions]);

        var hit = await stage.EvaluateAsync(new DecisionRequest(copy, "I was charged twice."));

        Assert.Equal(1, inner.Calls);
        Assert.True(hit.Value.IsCached);
        Assert.Same(copy, hit.Value.Definition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unreadable_or_unknown_version_L2_entry_is_a_miss_and_is_overwritten(bool unknownVersion)
    {
        // An unknown version is a valid entry for this definition in every field but "v", so only the version check
        // can turn it away.
        var entry = unknownVersion
            ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(
                CachedDecisionFormat.Write(CachedDecision.From(Responses.Answer().AsCached()))).Replace("\"v\":1,", "\"v\":2,", StringComparison.Ordinal))
            : "not json"u8.ToArray();
        Assert.Equal(unknownVersion, Encoding.UTF8.GetString(entry).StartsWith("{\"v\":2,", StringComparison.Ordinal));
        var distributed = new CountingDistributedCache();
        distributed.Seed(Key(), entry);
        using var memory = Responses.Memory();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory, distributed);

        var first = await stage.EvaluateAsync(Responses.Request());
        using var otherMemory = Responses.Memory();
        var otherInner = new ScriptedClient(TypeSafe);
        using var other = new CachingDecisionClient(otherInner, otherMemory, distributed);
        var second = await other.EvaluateAsync(Responses.Request());

        Assert.True(first.IsSuccess);
        Assert.False(first.Value.IsCached);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(1, distributed.WriteCount);
        Assert.True(second.Value.IsCached);
        Assert.Equal(0, otherInner.Calls);
    }

    [Fact]
    public async Task The_key_prefix_and_expiry_options_apply()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        using var stage = new CachingDecisionClient(
            new ScriptedClient(TypeSafe), memory, distributed, new DecisionCacheOptions { KeyPrefix = "app1:", Expiration = TimeSpan.FromHours(1) });

        await stage.EvaluateAsync(Responses.Request());

        Assert.NotNull(distributed.Peek(Key("app1:")));
        Assert.Null(distributed.Peek(Key()));
        Assert.Equal(TimeSpan.FromHours(1), distributed.Writes[Key("app1:")].AbsoluteExpirationRelativeToNow);
        Assert.True(memory.TryGetValue(Key("app1:"), out _));
        Assert.False(memory.TryGetValue(Key(), out _));
    }

    [Fact]
    public async Task The_default_expiry_is_five_minutes_in_L2()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        using var stage = new CachingDecisionClient(new ScriptedClient(TypeSafe), memory, distributed);

        await stage.EvaluateAsync(Responses.Request());

        Assert.Equal(TimeSpan.FromMinutes(5), distributed.Writes[Key()].AbsoluteExpirationRelativeToNow);
    }

    [Theory]
    [InlineData(null, 600)]
    [InlineData(30, 30)]
    public async Task The_local_expiry_is_LocalCacheExpiration_or_else_Expiration(int? localSeconds, int expectedSeconds)
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        using var memory = new MemoryCache(new MemoryCacheOptions { Clock = new SystemClockOver(clock) });
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(
            inner,
            memory,
            options: new DecisionCacheOptions
            {
                Expiration = TimeSpan.FromSeconds(600),
                LocalCacheExpiration = localSeconds is { } local ? TimeSpan.FromSeconds(local) : null,
            });
        await stage.EvaluateAsync(Responses.Request());

        clock.Advance(TimeSpan.FromSeconds(expectedSeconds - 1));
        await stage.EvaluateAsync(Responses.Request());
        Assert.Equal(1, inner.Calls);
        clock.Advance(TimeSpan.FromSeconds(2));
        await stage.EvaluateAsync(Responses.Request());

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task A_memory_cache_with_a_size_limit_stores_each_entry_at_size_one()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10, TrackStatistics = true });
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory);

        await stage.EvaluateAsync(Responses.Request());
        await stage.EvaluateAsync(Responses.Request("I was charged once."));
        var hit = await stage.EvaluateAsync(Responses.Request());

        Assert.True(hit.Value.IsCached);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(2, memory.GetCurrentStatistics()!.CurrentEstimatedSize);
    }

    [Fact]
    public async Task A_failing_L2_read_is_a_miss_that_logs_event_1007()
    {
        using var memory = Responses.Memory();
        var distributed = new FailingDistributedCache();
        var logger = new FakeLogger();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory, distributed, logger: logger);

        var result = await stage.EvaluateAsync(Responses.Request());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsCached);
        Assert.Equal(1, inner.Calls);
        var read = Assert.Single(logger.Collector.GetSnapshot(), record => record.Id.Id == 1007);
        Assert.Equal("CacheReadFailed", read.Id.Name);
        Assert.Equal(LogLevel.Warning, read.Level);
        Assert.Equal("typesafe", read.StructuredState!.Single(pair => string.Equals(pair.Key, "Provider", StringComparison.Ordinal)).Value);
        Assert.IsType<InvalidOperationException>(read.Exception);
    }

    [Fact]
    public async Task A_failing_L2_write_returns_the_answer_uncached_logs_event_1008_and_keeps_it_in_L1()
    {
        using var memory = Responses.Memory();
        var distributed = new FailingDistributedCache();
        var logger = new FakeLogger();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory, distributed, logger: logger);

        var result = await stage.EvaluateAsync(Responses.Request());
        var callsAfterMiss = distributed.Calls;
        var hit = stage.EvaluateAsync(Responses.Request());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsCached);
        Assert.Equal(2, callsAfterMiss);
        var write = Assert.Single(logger.Collector.GetSnapshot(), record => record.Id.Id == 1008);
        Assert.Equal("CacheWriteFailed", write.Id.Name);
        Assert.Equal(LogLevel.Warning, write.Level);
        Assert.IsType<InvalidOperationException>(write.Exception);
        Assert.True(hit.IsCompletedSuccessfully);
        Assert.True((await hit).Value.IsCached);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(callsAfterMiss, distributed.Calls);
    }

    [Fact]
    public async Task A_failing_L2_without_a_logger_still_answers()
    {
        using var memory = Responses.Memory();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory, new FailingDistributedCache());

        var result = await stage.EvaluateAsync(Responses.Request());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Cancelling_during_the_L2_read_throws()
    {
        using var memory = Responses.Memory();
        var logger = new FakeLogger();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory, new StalledDistributedCache(), logger: logger);
        using var cancellation = new CancellationTokenSource();

        var pending = stage.EvaluateAsync(Responses.Request(), cancellation.Token).AsTask();
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, inner.Calls);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Id.Id == 1007);
    }

    [Fact]
    public async Task An_L2_read_cancelled_by_the_cache_itself_is_a_miss_that_logs_event_1007()
    {
        using var memory = Responses.Memory();
        var logger = new FakeLogger();
        var inner = new ScriptedClient(TypeSafe);
        using var stage = new CachingDecisionClient(inner, memory, new TimingOutDistributedCache(), logger: logger);
        using var cancellation = new CancellationTokenSource();

        var result = await stage.EvaluateAsync(Responses.Request(), cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsCached);
        Assert.Equal(1, inner.Calls);
        var read = Assert.Single(logger.Collector.GetSnapshot(), record => record.Id.Id == 1007);
        Assert.IsType<OperationCanceledException>(read.Exception);
    }

    [Fact]
    public async Task Without_metadata_the_provider_is_unknown_and_the_cache_still_works()
    {
        using var memory = Responses.Memory();
        var inner = new ScriptedClient();
        using var stage = new CachingDecisionClient(inner, memory);

        await stage.EvaluateAsync(Responses.Request());
        var hit = await stage.EvaluateAsync(Responses.Request());

        Assert.True(hit.Value.IsCached);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Disposing_the_stage_disposes_neither_cache()
    {
        using var memory = Responses.Memory();
        var distributed = new CountingDistributedCache();
        var stage = new CachingDecisionClient(new ScriptedClient(TypeSafe), memory, distributed);
        await stage.EvaluateAsync(Responses.Request());

        stage.Dispose();

        Assert.True(memory.TryGetValue(Key(), out _));
    }

    [Fact]
    public async Task The_stage_refuses_null()
    {
        using var memory = Responses.Memory();
        using var stage = new CachingDecisionClient(new ScriptedClient(), memory);

        Assert.Throws<ArgumentNullException>(() => new CachingDecisionClient(new ScriptedClient(), null!));
        Assert.Throws<ArgumentNullException>(() => new CachingDecisionClient(null!, memory));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await stage.EvaluateAsync(null!));
    }

    /// <summary>A distributed cache whose reads wait until the caller's token is cancelled.</summary>
    private sealed class StalledDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new NotSupportedException();

        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => throw new NotSupportedException();

        public void Refresh(string key) => throw new NotSupportedException();

        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();

        public void Remove(string key) => throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// A distributed cache whose reads time out on its own token, as a Redis client's do, while the caller's token is not
    /// cancelled. Writes succeed.
    /// </summary>
    private sealed class TimingOutDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new NotSupportedException();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromException<byte[]?>(new OperationCanceledException("The cache read timed out."));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
            => Task.CompletedTask;

        public void Refresh(string key) => throw new NotSupportedException();

        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();

        public void Remove(string key) => throw new NotSupportedException();

        public Task RemoveAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
    }

    /// <summary>The in-process cache's clock, driven by a fake time provider.</summary>
    private sealed class SystemClockOver(TimeProvider time) : Microsoft.Extensions.Internal.ISystemClock
    {
        public DateTimeOffset UtcNow => time.GetUtcNow();
    }
}
