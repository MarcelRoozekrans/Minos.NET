using System.Text.Json;

namespace Minos.Docs.Tests;

/// <summary>The claims the testing page makes about fakes and canned responses.</summary>
public sealed class TestingYourCodeTests
{
    [Fact]
    public async Task EveryTypedOverload_GoesThroughTheFakesOneEvaluateMethod()
    {
        IDecisionClient jev = FakeDecision.Answering(0.9, TriageDesk.Billing, 0.8);

        Assert.True((await jev.EvaluateAsync<TriageQuestions>("text")).IsSuccess);
        Assert.True((await jev.EvaluateAsync<TriageQuestions>("text", CancellationToken.None)).IsSuccess);
        Assert.True((await jev.EvaluateUtf8Async<TriageQuestions>("\"text\""u8.ToArray())).IsSuccess);
        Assert.True((await jev.EvaluateAsync<TriageQuestions>(JsonElement.Parse("\"text\""))).IsSuccess);
        Assert.True((await jev.EvaluateAsync<TriageQuestions>(JsonElement.Parse("{}"), CancellationToken.None)).IsSuccess);

        Assert.Equal(5, ((FakeDecision)jev).Requests.Count);
    }

    [Fact]
    public async Task ATwoWordOption_IsKeyedInSnakeCase_ByTheFake()
    {
        IDecisionClient jev = FakeDecision.Answering(0.1, TriageDesk.ProductTeam, 0.9);

        var result = await jev.EvaluateAsync<TriageQuestions>("text");

        Assert.True(result.IsSuccess);
        Assert.Equal(TriageDesk.ProductTeam, result.Value.Desk.Value);
    }

    [Fact]
    public async Task ATypedCall_ReadsOnlyTheAnswersOfACannedBody()
    {
        const string body = """
            { "answers": { "is_urgent": { "type": "noul", "noul": 0.9 },
                           "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }
            """;

        var answers = await CannedDecision.EvaluateAsync<TriageQuestions>(body, "text");

        Assert.True(answers.IsUrgent.Value);
        Assert.Equal(TriageDesk.Billing, answers.Desk.Value);
    }

    [Theory]
    [InlineData("""{ "answers": { "is_urgent": { "noul": 0.9 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }""")]
    [InlineData("""{ "answers": { "is_urgent": { "type": "noul", "noul": 0.9 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 } } } }""")]
    [InlineData("""{ "answers": { "is_urgent": { "type": "noul", "noul": 0.9 } } }""")]
    public async Task ACannedBodyThatLeavesOutAKeyField_IsAnInvalidResponse(string body)
    {
        var (http, jev, _) = CannedDecision.Client(body);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync<TriageQuestions>("text", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(DecisionErrorKind.InvalidResponse, result.Error.Kind);
        }
    }

    [Fact]
    public async Task ARealClientWithoutABaseAddress_NeedsNoNetwork()
    {
        var handler = new CannedHandler(System.Net.HttpStatusCode.OK, """{ "answers": { "is_urgent": { "type": "noul", "noul": 0.1 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }""");
        using var http = new HttpClient(handler);

        Assert.Null(http.BaseAddress);
        using var jev = new DecisionClient(http, new DecisionClientOptions { ApiKey = "test-key", MaxRetries = 0 });

        Assert.NotNull(http.BaseAddress);
        Assert.Equal(TriageRoute.Queue, await new TicketTriager(jev).RouteAsync("text", CancellationToken.None));
    }
}
