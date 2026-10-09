using System.Net;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

/// <summary>Matches <c>request-noul.json</c> and <c>response-noul.json</c> exactly, for the typed round trip below.</summary>
[JevQuestions]
public partial record NoulTriage
{
    [Noul("Does this convey urgency?", WhenTrue = "Explicitly time-sensitive", WhenFalse = "No urgency expressed")]
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
                .WithBody("""{"model":"jev-1.13.0","answers":{"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.9,"technical":0.05,"sales":0.03,"other":0.02},"confidence":0.9},"severity":{"type":"score","score":0.2,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.8,"1":0.2},"confidence":0.8}},"usage":{"input_tokens":300,"output_tokens":20}}"""));

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
    public async Task EvaluateAsync_BuiltSet_SendsJsonInstructions()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"model":"jev-1.13.0","answers":{"is_duplicate":{"type":"noul","noul":0.9}},"usage":{"input_tokens":300,"output_tokens":20}}"""));

        // Declared sets are text only, so structured instructions come from a built set.
        var built = JevQuestionSet.CreateBuilder()
            .Noul("is_duplicate", JevContent.FromUtf8Json("""
                {
                  "potential_duplicate": { "name": "John Smith", "location": "Oakland, California", "last_employer": "Google" },
                  "question": "Is the resume for the same person as `potential_duplicate`?"
                }
                """u8), out var duplicate)
            .Build();
        Assert.True(built.IsSuccess);

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync(built.Value, "Resume: John Smith, Oakland, CA, last employer Google");

        Assert.True(result.IsSuccess);
        Assert.Equal(0.9, result.Value.Get(duplicate).Probability);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var entry = Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
        var fixture = JsonNode.Parse(Fixture.Text("request-structured.json"));
        var expected = fixture?["questions"];
        var actual = JsonNode.Parse(entry.RequestMessage!.Body!)?["questions"];
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }

    [Fact]
    public async Task EvaluateAsync_BuiltSet_SendsItsQuestions_AndReadsItsAnswers()
    {
        _fixture.Server
            .Given(Request.Create().WithPath("/v1/systemone").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95},"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.9,"technical":0.05,"sales":0.03,"other":0.02},"confidence":0.9},"product":{"type":"choice","choice":"pro-plan","probabilities":{"pro-plan":0.7,"team-plan":0.2,"other":0.1},"confidence":0.7},"effort":{"type":"score","score":0.9,"legend":{"0":"Minutes","1":"Hours","2":"Days"},"probabilities":{"0":0.3,"1":0.5,"2":0.2},"confidence":0.6}},"usage":{"input_tokens":300,"output_tokens":20}}"""));

        var built = JevQuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out var urgent, c => c
                .WhenTrue("Explicitly time-sensitive")
                .WhenFalse("No urgency expressed"))
            .Choice<IntegrationDepartment>("department", "Which team should handle this?", out var department, o => o
                .Describe(IntegrationDepartment.Billing, JevCriterion.Text("Payments, invoicing, refunds")
                    .WithExamples("I was charged twice")
                    .WithNotFor("How much is Pro?"))
                .Describe(IntegrationDepartment.Technical, JevCriterion.Text("Bugs, outages, integrations").WithExamples("The API returns 500"))
                .Describe(IntegrationDepartment.Sales, "Pricing, upgrades, new accounts"))
            .Choice("product", "Which product is `message` about?", out var product, o => o
                .Option("pro-plan", "The Pro subscription")
                .Option("team-plan", "The Team subscription")
                .Option("other"))
            .Score("effort", "How much effort will this take?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);

        using var client = IntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync(built.Value, "Help! My payouts have been failing for 3 days.");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Get(urgent).Value);
        Assert.Equal(IntegrationDepartment.Billing, result.Value.Get(department).Value);
        Assert.Equal("pro-plan", result.Value.Get(product).Value);
        var departmentAnswer = result.Value.Get(department);
        Assert.Equal(0.9, departmentAnswer.Confidence);
        Assert.Equal(4, departmentAnswer.Probabilities.Count);
        Assert.Equal(0.9, departmentAnswer.Probabilities[IntegrationDepartment.Billing]);
        Assert.Equal(0.05, departmentAnswer.Probabilities[IntegrationDepartment.Technical]);
        Assert.Equal(0.03, departmentAnswer.Probabilities[IntegrationDepartment.Sales]);
        Assert.Equal(0.02, departmentAnswer.Probabilities[IntegrationDepartment.Other]);
        var productAnswer = result.Value.Get(product);
        Assert.Equal(0.7, productAnswer.Confidence);
        Assert.Equal(3, productAnswer.Probabilities.Count);
        Assert.Equal(0.7, productAnswer.Probabilities["pro-plan"]);
        Assert.Equal(0.2, productAnswer.Probabilities["team-plan"]);
        Assert.Equal(0.1, productAnswer.Probabilities["other"]);
        var effortAnswer = result.Value.Get(effort);
        Assert.Equal(1, effortAnswer.Level);
        Assert.Equal(0.9, effortAnswer.Expected);
        Assert.Equal(0.6, effortAnswer.Confidence);
        Assert.Equal(3, effortAnswer.Probabilities.Count);
        Assert.Equal(0.3, effortAnswer.Probabilities[0]);
        Assert.Equal(0.5, effortAnswer.Probabilities[1]);
        Assert.Equal(0.2, effortAnswer.Probabilities[2]);
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var entry = Assert.Single(_fixture.Server.LogEntries);
#pragma warning restore HLQ005
        var expected = JsonNode.Parse(Fixture.Text("request-built-set.json"));
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(entry.RequestMessage!.Body!)));
    }
}
