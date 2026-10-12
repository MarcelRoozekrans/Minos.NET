using System.Net;
using System.Text.Json.Nodes;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

/// <summary>
/// One generated question set, <see cref="OpenAITicket"/>, sent through both protocol adapters: the generator's output
/// carries no wire JSON of its own, and each adapter writes the set in its protocol's shape.
/// </summary>
public sealed class ProtocolParityTests : IClassFixture<WireMockFixture>
{
    private const string State = "I was charged twice.";

    private static readonly string[] QuestionNames = ["refund", "topic", "urgency"];

    private readonly WireMockFixture _fixture;

    public ProtocolParityTests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task OneQuestionSet_IsWrittenByEachAdapter_AndAnswersTheSame()
    {
        Respond("/v1/systemone", Fixture.Text("Parity/response-all-kinds.systemone.json"));
        Respond("/v1/decisions", Fixture.Text("OpenAI/success-all-kinds.json"));
        using var systemOne = IntegrationClient.Create(_fixture.BaseAddress);
        using var openAI = OpenAIIntegrationClient.Create(_fixture.BaseAddress);

        var viaSystemOne = await systemOne.EvaluateAsync<OpenAITicket>(State);
        var viaOpenAI = await openAI.EvaluateAsync<OpenAITicket>(State);

        Assert.True(viaSystemOne.IsSuccess, viaSystemOne.IsFailure ? viaSystemOne.Error.Message : null);
        Assert.True(viaOpenAI.IsSuccess, viaOpenAI.IsFailure ? viaOpenAI.Error.Message : null);
        Assert.Equal(viaSystemOne.Value.Refund.Probability, viaOpenAI.Value.Refund.Probability);
        Assert.Equal(viaSystemOne.Value.Topic.Value, viaOpenAI.Value.Topic.Value);
        Assert.Equal(viaSystemOne.Value.Urgency.Value, viaOpenAI.Value.Urgency.Value);

        var systemOneBody = JsonNode.Parse(SentBody("/v1/systemone"))!;
        var openAIBody = JsonNode.Parse(SentBody("/v1/decisions"))!;
        Assert.True(
            JsonNode.DeepEquals(Fixture.Load("Parity/request-all-kinds.systemone.json"), systemOneBody),
            systemOneBody.ToJsonString());
        Assert.True(
            JsonNode.DeepEquals(Fixture.Load("Parity/request-all-kinds.openai.json"), openAIBody),
            openAIBody.ToJsonString());

        // DeepEquals ignores the order of an object's properties, so the question order is checked on its own:
        // System One keys its questions by name, OpenAI lists them, and both keep the set's order.
        Assert.Equal(QuestionNames, systemOneBody["questions"]!.AsObject().Select(question => question.Key));
        Assert.Equal(QuestionNames, openAIBody["questions"]!.AsArray().Select(question => (string)question!["name"]!));
    }

    private void Respond(string path, string body)
        => _fixture.Server
            .Given(Request.Create().WithPath(path).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

    private string SentBody(string path)
    {
        var sent = Assert.Single(_fixture.Server.LogEntries, entry => string.Equals(entry.RequestMessage!.Path, path, StringComparison.Ordinal));
        return sent.RequestMessage!.Body!;
    }
}
