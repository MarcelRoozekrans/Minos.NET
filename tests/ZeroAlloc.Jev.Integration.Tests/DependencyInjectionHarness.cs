using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Jev.Integration.Tests;

/// <summary>Builds the <c>AddJevClient</c> provider the dependency injection integration tests share.</summary>
internal static class DependencyInjectionHarness
{
    // One retry, a short backoff and no jitter, through IntegrationClient.Configure as the hand-built clients are;
    // timeout null keeps the library's default.
    public static ServiceProvider Provider(WireMockFixture fixture, AttemptCount attempts, TimeSpan? timeout)
    {
        var services = new ServiceCollection();
        services
            .AddJevClient(options =>
            {
                IntegrationClient.Configure(options, maxRetries: 1);
                options.BaseAddress = fixture.BaseAddress;
                if (timeout is { } perAttempt)
                {
                    options.Timeout = perAttempt;
                }
            })
            .AddHttpMessageHandler(() => new CountingHandler(attempts));
        return services.BuildServiceProvider();
    }

    /// <summary>How many attempts have started.</summary>
    internal sealed class AttemptCount
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }

    /// <summary>Counts each attempt as it starts, then passes it on.</summary>
    private sealed class CountingHandler(AttemptCount attempts) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            attempts.Increment();
            return base.SendAsync(request, cancellationToken);
        }
    }
}
