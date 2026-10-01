using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ZeroAlloc.Jev.Integration.Tests;

/// <summary>
/// A client from <c>AddJevClient</c> over WireMock, sending through the factory's <see cref="HttpClient"/> and its default
/// primary handler. Attempts are counted on the client, by a handler added through the returned builder, so no test
/// here reads WireMock's log.
/// </summary>
public sealed class DependencyInjectionTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public DependencyInjectionTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task RetriedServiceUnavailable_Succeeds()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("di-retry")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("di-retry")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));
        var attempts = new AttemptCount();
        using var provider = Provider(attempts, timeout: null);

        var result = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(0.95, Assert.IsType<NoulAnswer>(result.Value.Answers["is_urgent"]).Noul, 3);
        Assert.Equal(2, attempts.Value);
    }

    [Fact]
    public async Task SlowResponse_TimesOutPerAttempt_WithTheOptionsTimeout()
    {
        // The server holds every response until the test has its result, so an attempt can only end through the
        // options' per-attempt time-out, which the factory's HttpClient got from JevClient.ConfigureHttpClient.
        using var held = _fixture.HoldEveryResponse();
        var attempts = new AttemptCount();
        using var provider = Provider(attempts, timeout: TimeSpan.FromMilliseconds(300));

        var result = await provider.GetRequiredService<IJevClient>().EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Timeout, result.Error.Kind);
        Assert.Equal(2, attempts.Value);
    }

    // One retry, a short backoff and no jitter, through IntegrationClient.Configure as the hand-built clients are;
    // timeout null keeps the library's default.
    private ServiceProvider Provider(AttemptCount attempts, TimeSpan? timeout)
    {
        var services = new ServiceCollection();
        services
            .AddJevClient(options =>
            {
                IntegrationClient.Configure(options, maxRetries: 1);
                options.BaseAddress = _fixture.BaseAddress;
                if (timeout is { } perAttempt)
                {
                    options.Timeout = perAttempt;
                }
            })
            .AddHttpMessageHandler(() => new CountingHandler(attempts));
        return services.BuildServiceProvider();
    }

    /// <summary>How many attempts have started.</summary>
    private sealed class AttemptCount
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
