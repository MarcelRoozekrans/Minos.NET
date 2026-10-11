using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Minos.Integration.Tests;

public enum OpenAITopic
{
    [Criteria("Payments and invoices")]
    Billing,

    Other,
}

public enum OpenAIUrgency
{
    [Level("Not urgent")]
    NotUrgent,

    [Level("Soon")]
    Soon,

    [Level("Right now")]
    RightNow,
}

/// <summary>The set the OpenAI fixtures answer: <c>refund</c>, <c>topic</c> and <c>urgency</c>.</summary>
[Questions]
public partial record OpenAITicket
{
    [Noul("Is this a refund request?")]
    public partial Noul Refund { get; }

    [Choice("Which topic is this about?")]
    public partial Choice<OpenAITopic> Topic { get; }

    [Score("How urgent is this?")]
    public partial Score<OpenAIUrgency> Urgency { get; }
}

/// <summary>
/// <see cref="Minos.OpenAI.OpenAIDecisionClient"/> against WireMock at <c>/v1/decisions</c>, through the public API,
/// with the fixtures built from OpenAI's Decisions guide.
/// </summary>
public sealed class OpenAITests : IClassFixture<WireMockFixture>
{
    private const string Path = "/v1/decisions";

    private readonly WireMockFixture _fixture;

    public OpenAITests(WireMockFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    [Fact]
    public async Task AllKinds_Succeeds_WithTheTypedAnswers()
    {
        Respond(HttpStatusCode.OK, Fixture.Text("OpenAI/success-all-kinds.json"));
        using var client = OpenAIIntegrationClient.Create(_fixture.BaseAddress);

        var result = await client.EvaluateAsync<OpenAITicket>("I was charged twice.");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(0.92, result.Value.Refund.Probability);
        Assert.Equal(OpenAITopic.Billing, result.Value.Topic.Value);
        Assert.Equal(OpenAIUrgency.Soon, result.Value.Urgency.Value);

        var request = OnlyLoggedRequest();
        Assert.Equal("Bearer integration-key", request.Headers!["Authorization"][0]);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("gpt-6-luna", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("I was charged twice.", body.RootElement.GetProperty("input").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("questions").GetArrayLength());
    }

    [Fact]
    public async Task ARefusal_IsRefused_AndNotRetried()
    {
        Respond(HttpStatusCode.OK, Fixture.Text("OpenAI/refusal.json"));
        using var client = OpenAIIntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync(new DecisionRequest(WithOtherQuestion(), DecisionContent.FromString("x")));

        Assert.Equal(DecisionErrorKind.Refused, result.Error.Kind);
        Assert.Equal("The model refused to answer: other_question.", result.Error.Message);
        Assert.Equal(200, result.Error.StatusCode);
        _ = OnlyLoggedRequest();
    }

    [Fact]
    public async Task A401_WithAnOpenAIErrorBody_IsUnauthorized_WithItsMessage()
    {
        Respond(
            HttpStatusCode.Unauthorized,
            """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","param":null,"code":"invalid_api_key"}}""");
        using var client = OpenAIIntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync<OpenAITicket>("x");

        Assert.Equal(DecisionErrorKind.Unauthorized, result.Error.Kind);
        Assert.Equal(401, result.Error.StatusCode);
        Assert.Equal("Incorrect API key provided.", result.Error.Message);
        Assert.Equal("invalid_api_key", result.Error.Detail!.Value.GetProperty("error").GetProperty("code").GetString());
        _ = OnlyLoggedRequest();
    }

    [Fact]
    public async Task A429_IsRetried_ThenSucceeds()
    {
        _fixture.Server
            .Given(Request.Create().WithPath(Path).UsingPost())
            .InScenario("openai-429")
            .WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.TooManyRequests));
        _fixture.Server
            .Given(Request.Create().WithPath(Path).UsingPost())
            .InScenario("openai-429")
            .WhenStateIs("retried")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBody(Fixture.Text("OpenAI/success-all-kinds.json")));
        using var client = OpenAIIntegrationClient.Create(_fixture.BaseAddress, maxRetries: 2);

        var result = await client.EvaluateAsync<OpenAITicket>("I was charged twice.");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(2, _fixture.Server.LogEntries.Count);
    }

    [Fact]
    public async Task AnImageRequest_SendsTheInputImagePart()
    {
        Respond(HttpStatusCode.OK, Fixture.Text("OpenAI/success-all-kinds.json"));
        using var client = OpenAIIntegrationClient.Create(_fixture.BaseAddress);
        DecisionImage[] images = [DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")];

        var result = await client.EvaluateAsync<OpenAITicket>("Is this receipt a refund?", images);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var body = OnlyLoggedRequest().Body!;
        Assert.Contains("\"type\":\"input_image\"", body, StringComparison.Ordinal);
        Assert.Contains("\"image_url\":\"data:image/png;base64,iVBORw==\"", body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"input_text\",\"text\":\"Is this receipt a refund?\"", body, StringComparison.Ordinal);
    }

    private static QuestionSetDefinition WithOtherQuestion() => new(
        QuestionDefinition.Noul("refund", "Is this a refund request?"),
        QuestionDefinition.Choice(
            "topic",
            "Which topic is this about?",
            new OptionDefinition("billing", "Payments and invoices"),
            new OptionDefinition("other", null)),
        QuestionDefinition.Score("urgency", "How urgent is this?", "Not urgent", "Soon", "Right now"),
        QuestionDefinition.Noul("other_question", "Is anything else asked?"));

    private void Respond(HttpStatusCode status, string body)
        => _fixture.Server
            .Given(Request.Create().WithPath(Path).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", "application/json").WithBody(body));

    private WireMock.IRequestMessage OnlyLoggedRequest()
        => _fixture.Server.LogEntries.ToArray() is [var only]
            ? only.RequestMessage!
            : throw new InvalidOperationException($"Expected one request, found {_fixture.Server.LogEntries.Count}.");
}
