using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

public sealed class RetryTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public RetryTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task RetryAfterOneSecond_IsHonoured()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("retry-after")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.TooManyRequests)
                .WithHeader("Retry-After", "1"));

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("retry-after")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        // The wait is timed on the client. WireMock stamps a request when it has read it, and on a loaded machine
        // that can lag the send by seconds: a late stamp on the first request shrinks the gap WireMock sees between
        // the two, even though the client waited the full second.
        using var attempts = new AttemptRecorder(_fixture.BaseAddress);
        using var client = IntegrationClient.Create(attempts, maxRetries: 2);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        // WireMock logs a request before it writes the response, so both are in the log once the call has returned.
        Assert.Equal(2, _fixture.Server.LogEntries.Count);
        Assert.Equal(2, attempts.Count);

        // 900 rather than 1000 ms leaves room for the coarse system timer behind the client's wait; delays on a busy
        // machine only lengthen the gap.
        var gap = attempts.Gap(0);
        Assert.True(
            gap >= TimeSpan.FromMilliseconds(900),
            $"Expected at least 900 ms between attempts, got {gap.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task RetryCount_IsSentOnRetries()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("retry-count")
            .WillSetStateTo("retried-once")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("retry-count")
            .WhenStateIs("retried-once")
            .WillSetStateTo("retried-twice")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("retry-count")
            .WhenStateIs("retried-twice")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        var retryCounts = _fixture.Server.LogEntries
            .OrderBy(entry => entry.RequestMessage!.DateTime)
            .Select(entry => entry.RequestMessage!.Headers!.TryGetValue("X-TypeSafe-Retry-Count", out var values) ? values[0] : null)
            .ToList();
        Assert.Equal([null, "1", "2"], retryCounts);
    }

    [Fact]
    public async Task PersistentOverload_MakesMaxRetriesPlusOneAttempts()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));

        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Overloaded, result.Error.Kind);
        Assert.Equal(3, _fixture.Server.LogEntries.Count);
    }

    [Fact]
    public async Task BadRequest_IsNotRetried()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.BadRequest));

        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Validation, result.Error.Kind);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
    }
}
