using System.Net;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ZeroAlloc.Jev.Integration.Tests;

/// <summary>Matches <c>request-noul.json</c> and <c>response-noul.json</c> exactly, for the typed round trip below.</summary>
[JevQuestions]
public partial record NoulTriage
{
    [Noul("Does this convey urgency?", True = "Explicitly time-sensitive", False = "No urgency expressed")]
    public partial Noul IsUrgent { get; }
}

public sealed class TypedEvaluationTests : IClassFixture<WireMockFixture>
{
    private readonly WireMockFixture _fixture;

    public TypedEvaluationTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task EvaluateAsyncT_RetriesThenParsesTypedAnswers()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("typed-retry")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.ServiceUnavailable));

        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .InScenario("typed-retry")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("response-noul.json")));

        using var client = IntegrationClient.Create(_fixture.BaseAddress, maxRetries: 1);

        var result = await client.EvaluateAsync<NoulTriage>("Help! My payouts have been failing for 3 days.");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsUrgent.Value);
        Assert.Equal(0.95, result.Value.IsUrgent.Probability);

        var entries = _fixture.Server.LogEntries.OrderBy(entry => entry.RequestMessage!.DateTime).ToList();
        Assert.Equal(2, entries.Count);

        var retryCounts = entries
            .Select(entry => entry.RequestMessage!.Headers!.TryGetValue("X-TypeSafe-Retry-Count", out var values) ? values[0] : null)
            .ToList();
        Assert.Equal([null, "1"], retryCounts);

        var expectedBody = JsonNode.Parse(Fixture.Text("request-noul.json"));
        Assert.All(entries, entry => Assert.True(JsonNode.DeepEquals(expectedBody, JsonNode.Parse(entry.RequestMessage!.Body!))));
    }
}
