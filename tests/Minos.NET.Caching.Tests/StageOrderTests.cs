using System.Threading.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Minos.RateLimiting;

namespace Minos.Caching.Tests;

public sealed class StageOrderTests
{
    [Fact]
    public async Task In_the_recommended_order_a_cache_hit_takes_no_permit()
    {
        using var services = Responses.Services();
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
            Window = TimeSpan.FromHours(1),
            AutoReplenishment = false,
        });
        var inner = new ScriptedClient();
        using var client = inner.AsBuilder()
            .UseOpenTelemetry()
            .UseCaching()
            // Retries off: the third call's rejection would wait out MaxRetryDelay on the real clock. Task 3 covers a
            // retry stage retrying a rejection.
            .UseRetries(o => o.MaxRetries = 0)
            .UseRateLimiting(limiter)
            .Build(services);

        var miss = await client.EvaluateAsync(Responses.Request());
        var hit = await client.EvaluateAsync(Responses.Request());
        var otherState = await client.EvaluateAsync(Responses.Request("Another message."));

        Assert.False(miss.Value.IsCached);
        Assert.True(hit.Value.IsCached);
        Assert.Equal(DecisionErrorKind.RateLimited, otherState.Error.Kind);
        // A local rejection: a server's 429 carries a status code.
        Assert.Null(otherState.Error.StatusCode);
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task In_the_recommended_order_every_attempt_takes_a_permit()
    {
        var time = new FakeTimeProvider();
        var collection = new ServiceCollection();
        collection.AddSingleton<TimeProvider>(time);
        collection.AddMemoryCache();
        using var services = collection.BuildServiceProvider();
        using var counting = new CountingLimiter();
        var inner = new ScriptedClient().ThenFail(DecisionErrorKind.Server).ThenAnswer();
        using var client = inner.AsBuilder()
            .UseOpenTelemetry()
            .UseCaching()
            .UseRetries(o => o.Jitter = false)
            .UseRateLimiting(counting)
            .Build(services);

        var pending = client.EvaluateAsync(Responses.Request()).AsTask();
        // Step the clock until the backoff timer, which the retry stage starts after the first failure, has fired.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!pending.IsCompleted && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        var first = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        var repeat = await client.EvaluateAsync(Responses.Request());

        Assert.True(first.IsSuccess);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(2, counting.Acquires);
        Assert.True(repeat.Value.IsCached);
        Assert.Equal(2, counting.Acquires);
    }

    /// <summary>A limiter that always grants a permit and counts how often it was asked.</summary>
    private sealed class CountingLimiter : RateLimiter
    {
        private readonly ConcurrencyLimiter _inner = new(new ConcurrencyLimiterOptions { PermitLimit = 100, QueueLimit = 0 });
        private int _acquires;

        public int Acquires => Volatile.Read(ref _acquires);

        public override TimeSpan? IdleDuration => _inner.IdleDuration;

        public override RateLimiterStatistics? GetStatistics() => _inner.GetStatistics();

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Interlocked.Increment(ref _acquires);
            return _inner.AttemptAcquire(permitCount);
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquires);
            return _inner.AcquireAsync(permitCount, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
