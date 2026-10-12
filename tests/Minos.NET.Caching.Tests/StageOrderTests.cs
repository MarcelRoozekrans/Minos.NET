using System.Threading.RateLimiting;
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
            .UseHybridCache()
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
        Assert.Equal(RateLimitingDecisionClient.RejectedMessage, otherState.Error.Message);
        Assert.Equal(1, inner.Calls);
    }
}
