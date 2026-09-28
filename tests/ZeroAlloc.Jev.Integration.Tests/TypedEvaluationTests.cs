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

public enum IntegrationDepartment
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Bugs, outages, integrations", Examples = new[] { "The API returns 500" })]
    Technical,

    [Criteria("Pricing, upgrades, new accounts", Examples = [], NotFor = [])]
    Sales,

    Other,
}

public enum IntegrationSeverity
{
    [Level("Cosmetic", NotFor = ["Data loss"])]
    Low,

    [Level("Blocks work")]
    High,
}

/// <summary>Matches <c>request-structured-criteria.json</c> exactly.</summary>
[JevQuestions]
public partial record StructuredTriage
{
    [Choice("Which team should handle this?")]
    public partial Choice<IntegrationDepartment> Department { get; }

    [Score("How severe is this?")]
    public partial Score<IntegrationSeverity> Severity { get; }
}

/// <summary>Matches the questions of <c>request-structured.json</c>: object instructions sent with Json = true.</summary>
[JevQuestions]
public partial record DuplicateTriage
{
    [Noul(
        """
        {
          "potential_duplicate": {
            "name": "John Smith",
            "location": "Oakland, California",
            "last_employer": "Google"
          },
          "question": "Is the resume for the same person as `potential_duplicate`?"
        }
        """,
        Json = true)]
    public partial Noul IsDuplicate { get; }
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

    [Fact]
    public async Task EvaluateAsyncT_SendsStructuredCriteria()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"model":"jev-1.13.0","answers":{"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.9,"technical":0.05,"sales":0.03,"other":0.02},"confidence":0.9},"severity":{"type":"score","score":0.2,"probabilities":{"0":0.8,"1":0.2},"confidence":0.8}},"usage":{"input_tokens":300,"output_tokens":20}}"""));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync<StructuredTriage>("Help! My payouts have been failing for 3 days.");

        Assert.True(result.IsSuccess);
        Assert.Equal(IntegrationDepartment.Billing, result.Value.Department.Value);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var entry = Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
        var expected = JsonNode.Parse(Fixture.Text("request-structured-criteria.json"));
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(entry.RequestMessage!.Body!)));
    }

    [Fact]
    public async Task EvaluateAsyncT_SendsJsonInstructions()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"model":"jev-1.13.0","answers":{"is_duplicate":{"type":"noul","noul":0.9}},"usage":{"input_tokens":300,"output_tokens":20}}"""));

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync<DuplicateTriage>("Resume: John Smith, Oakland, CA, last employer Google");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.9, result.Value.IsDuplicate.Probability);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var entry = Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
        var fixture = JsonNode.Parse(Fixture.Text("request-structured.json"));
        var expected = fixture?["questions"];
        var actual = JsonNode.Parse(entry.RequestMessage!.Body!)?["questions"];
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }
}
