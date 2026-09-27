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
                .WithBody(Fixtures.Text("response-noul.json")));

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
