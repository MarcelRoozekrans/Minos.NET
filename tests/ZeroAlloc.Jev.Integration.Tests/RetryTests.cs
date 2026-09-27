using System.Net;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ZeroAlloc.Jev.Integration.Tests;

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

        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync(Fixtures.NoulRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _fixture.Server.LogEntries.Count);

        var timestamps = _fixture.Server.LogEntries
            .Select(entry => entry.RequestMessage!.DateTime)
            .OrderBy(dateTime => dateTime)
            .ToList();
        Assert.True(
            timestamps[1] - timestamps[0] >= TimeSpan.FromMilliseconds(900),
            $"Expected at least 900 ms between requests, got {(timestamps[1] - timestamps[0]).TotalMilliseconds} ms.");
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
        // The spec (and TypeSafe's Python SDK) calls for the header to be absent on the first attempt.
        // ZeroAlloc.Rest 2.2.0 cannot do that yet: a null [Header] argument still sends an empty-valued header
        // instead of omitting it (ZeroAlloc-Net/ZeroAlloc.Rest#354, closed upstream but not yet in a released
        // package). Once a release with that fix ships and this project upgrades to it, the first value below
        // becomes null with no code change on this side.
        Assert.Equal(["", "1", "2"], retryCounts);
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
