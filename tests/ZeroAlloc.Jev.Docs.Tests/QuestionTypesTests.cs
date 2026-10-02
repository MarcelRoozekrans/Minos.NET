namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class QuestionTypesTests
{
    // Mood: 0 x 0.6 + 1 x 0.3 + 2 x 0.1 = 0.5, so Expected is 0.5 and Normalized is 0.25 over three levels.
    private const string TicketResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.92 },
            "department": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.7, "technical": 0.2, "sales": 0.1 }, "confidence": 0.64 },
            "mood": { "type": "score", "score": 0.5, "legend": { "0": "Annoyed or angry", "1": "Neutral", "2": "Pleased or grateful" }, "probabilities": { "0": 0.6, "1": 0.3, "2": 0.1 }, "confidence": 0.7 }
          },
          "usage": { "input_tokens": 150, "output_tokens": 20 }
        }
        """;

    // Effort: 0 x 0.1 + 1 x 0.2 + 2 x 0.7 = 1.6, so Normalized is 0.8 over three levels.
    private const string KeyedResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "plan": { "type": "choice", "choice": "team-plan", "probabilities": { "free": 0.05, "pro-plan": 0.25, "team-plan": 0.7 }, "confidence": 0.66 },
            "effort": { "type": "score", "score": 1.6, "legend": { "0": "Minutes", "1": "Hours", "2": "Days" }, "probabilities": { "0": 0.1, "1": 0.2, "2": 0.7 }, "confidence": 0.8 }
          },
          "usage": { "input_tokens": 90, "output_tokens": 14 }
        }
        """;

    [Fact]
    public async Task ReadNoul_GivesTheProbability_AndValueFromHalfUp()
    {
        var analysis = await CannedJev.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        Assert.Equal((true, 0.92), AnswerReading.ReadNoul(analysis.IsUrgent));
        Assert.False(AnswerReading.ReadNoul(new Noul(0.49)).Yes);
        Assert.True(AnswerReading.ReadNoul(new Noul(0.5)).Yes);
    }

    [Fact]
    public async Task ReadChoice_GivesTheOption_TheConfidence_AndEveryProbability()
    {
        var analysis = await CannedJev.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        Assert.Equal((Department.Billing, 0.64, 0.7, 0.1), AnswerReading.ReadChoice(analysis.Department));
        Assert.Equal(Department.Technical, AnswerReading.RunnerUp(analysis.Department));
    }

    [Fact]
    public async Task ReadScore_GivesTheLevel_TheExpectedLevel_AndTheNormalizedOne()
    {
        var analysis = await CannedJev.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        Assert.Equal((Mood.Annoyed, 0.5, 0.25, 0.7), AnswerReading.ReadScore(analysis.Mood));
    }

    [Fact]
    public async Task KeyedAnswers_ReadByKeyAndByLevelIndex()
    {
        var advisor = new PlanAdvisor([("free", "No cost"), ("pro-plan", "One user"), ("team-plan", "Many users")]);
        var (http, jev, requests) = CannedJev.Client(KeyedResponse);
        using (http)
        using (jev)
        {
            var advice = await advisor.AdviseAsync(jev, "We are a team of twelve.", CancellationToken.None);

            Assert.Equal(("team-plan", 0.7, 2, 0.8), advice);
        }

        Assert.Collection(requests, body => Assert.Contains("\"effort\"", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadingAnswers_AllocatesNothing()
    {
        var analysis = await CannedJev.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        // Warm up, so one-time JIT work does not count.
        Read(analysis);

        var before = GC.GetAllocatedBytesForCurrentThread();
        Read(analysis);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void Read(TicketAnalysis analysis)
    {
        _ = AnswerReading.ReadNoul(analysis.IsUrgent);
        _ = AnswerReading.ReadChoice(analysis.Department);
        _ = AnswerReading.RunnerUp(analysis.Department);
        _ = AnswerReading.ReadScore(analysis.Mood);
    }
}
