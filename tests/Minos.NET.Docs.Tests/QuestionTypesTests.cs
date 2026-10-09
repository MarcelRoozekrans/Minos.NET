using ZeroAlloc.TestHelpers;

namespace Minos.Docs.Tests;

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
        var analysis = await CannedDecision.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        Assert.Equal((true, 0.92), AnswerReading.ReadNoul(analysis.IsUrgent));
        Assert.False(AnswerReading.ReadNoul(new Noul(0.49)).Yes);
        Assert.True(AnswerReading.ReadNoul(new Noul(0.5)).Yes);
    }

    [Fact]
    public async Task ReadChoice_GivesTheOption_TheConfidence_AndEveryProbability()
    {
        var analysis = await CannedDecision.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        Assert.Equal((Department.Billing, 0.64, 0.7, 0.1), AnswerReading.ReadChoice(analysis.Department));
        Assert.Equal(Department.Technical, AnswerReading.RunnerUp(analysis.Department));
    }

    [Fact]
    public async Task ReadScore_GivesTheLevel_TheExpectedLevel_AndTheNormalizedOne()
    {
        var analysis = await CannedDecision.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        Assert.Equal((Mood.Annoyed, 0.5, 0.25, 0.7), AnswerReading.ReadScore(analysis.Mood));
    }

    [Fact]
    public async Task KeyedAnswers_ReadByKeyAndByLevelIndex()
    {
        var advisor = new PlanAdvisor([("free", "No cost"), ("pro-plan", "One user"), ("team-plan", "Many users")]);
        var (http, client, requests) = CannedDecision.Client(KeyedResponse);
        using (http)
        using (client)
        {
            var advice = await advisor.AdviseAsync(client, "We are a team of twelve.", CancellationToken.None);

            Assert.Equal(("team-plan", 0.7, 2, 0.8), advice);
        }

        Assert.Collection(requests, body => Assert.Contains("\"effort\"", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadingAnswers_AllocatesNothing()
    {
        var analysis = await CannedDecision.EvaluateAsync<TicketAnalysis>(TicketResponse, "Help!");

        var built = QuestionSet.CreateBuilder()
            .Choice("plan", "Which plan fits this customer?", out var plan, o => o.Option("free").Option("pro-plan").Option("team-plan"))
            .Score("effort", "How much setup work does the customer need?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);
        var (http, client, _) = CannedDecision.Client(KeyedResponse);
        using var httpScope = http;
        using var clientScope = client;
        var evaluated = await client.EvaluateAsync(built.Value, "A team of twelve.", CancellationToken.None);
        Assert.True(evaluated.IsSuccess);
        var answers = evaluated.Value;

        var sum = 0.0;

        // Every read the page calls free: the typed Noul, Choice and Score reads, the keyed reads through
        // Answers.Get, and enumerating a keyed probability map.
        AllocationGate.AssertBudget(
            0,
            1000,
            () =>
            {
                sum += AnswerReading.ReadNoul(analysis.IsUrgent).Probability;
                sum += AnswerReading.ReadChoice(analysis.Department).PickedProbability;
                sum += AnswerReading.RunnerUp(analysis.Department) is null ? 0 : 1;
                sum += AnswerReading.ReadScore(analysis.Mood).Normalized;

                var keyedChoice = answers.Get(plan);
                sum += keyedChoice.Confidence + keyedChoice.Probabilities[keyedChoice.Value] + keyedChoice.Value.Length;
                foreach (var (key, probability) in keyedChoice.Probabilities)
                {
                    sum += probability + key.Length;
                }

                var keyedScore = answers.Get(effort);
                sum += keyedScore.Level + keyedScore.Expected + keyedScore.Normalized + keyedScore.Probabilities[1];
            },
            "ReadingAnswers");

        Assert.True(sum > 0);
    }
}
