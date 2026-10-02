namespace ZeroAlloc.Jev.Docs.Tests;

/// <summary>The claims the testing page makes about fakes and canned responses.</summary>
public sealed class TestingYourCodeTests
{
    [Fact]
    public async Task EveryTypedOverload_GoesThroughTheFakesOneEvaluateMethod()
    {
        IJevClient jev = FakeJev.Answering(0.9, TriageDesk.Billing, 0.8);

        Assert.True((await jev.EvaluateAsync<TriageQuestions>("text")).IsSuccess);
        Assert.True((await jev.EvaluateAsync<TriageQuestions>("text", CancellationToken.None)).IsSuccess);
        Assert.True((await jev.EvaluateUtf8Async<TriageQuestions>("\"text\""u8.ToArray())).IsSuccess);

        Assert.Equal(3, ((FakeJev)jev).Requests.Count);
    }

    [Fact]
    public async Task ATypedCall_ReadsOnlyTheAnswersOfACannedBody()
    {
        const string body = """
            { "answers": { "is_urgent": { "type": "noul", "noul": 0.9 },
                           "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }
            """;

        var answers = await CannedJev.EvaluateAsync<TriageQuestions>(body, "text");

        Assert.True(answers.IsUrgent.Value);
        Assert.Equal(TriageDesk.Billing, answers.Desk.Value);
    }

    [Theory]
    [InlineData("""{ "answers": { "is_urgent": { "noul": 0.9 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }""")]
    [InlineData("""{ "answers": { "is_urgent": { "type": "noul", "noul": 0.9 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 } } } }""")]
    [InlineData("""{ "answers": { "is_urgent": { "type": "noul", "noul": 0.9 } } }""")]
    public async Task ACannedBodyThatLeavesOutAKeyField_IsAnInvalidResponse(string body)
    {
        var (http, jev, _) = CannedJev.Client(body);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync<TriageQuestions>("text", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        }
    }

    [Fact]
    public async Task ARealClientWithoutABaseAddress_NeedsNoNetwork()
    {
        var handler = new CannedHandler(System.Net.HttpStatusCode.OK, """{ "answers": { "is_urgent": { "type": "noul", "noul": 0.1 }, "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 1 }, "confidence": 0.9 } } }""");
        using var http = new HttpClient(handler);

        Assert.Null(http.BaseAddress);
        using var jev = new JevClient(http, new JevClientOptions { ApiKey = "test-key", MaxRetries = 0 });

        Assert.NotNull(http.BaseAddress);
        Assert.Equal(TriageRoute.Queue, await new TicketTriager(jev).RouteAsync("text", CancellationToken.None));
    }
}
