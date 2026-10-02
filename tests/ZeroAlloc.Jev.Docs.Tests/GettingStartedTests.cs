namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class GettingStartedTests
{
    private const string TicketResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.93 },
            "team": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.81, "technical": 0.15, "sales": 0.04 }, "confidence": 0.77 }
          },
          "usage": { "input_tokens": 120, "output_tokens": 12 }
        }
        """;

    [Fact]
    public async Task TriageAsync_ReadsTheTypedAnswers()
    {
        var (http, jev, requests) = CannedJev.Client(TicketResponse);
        using (http)
        using (jev)
        {
            var summary = await GettingStartedEvaluation.TriageAsync(jev, "Help! My payouts have been failing for 3 days.", CancellationToken.None);

            Assert.Equal("urgent, for Billing", summary);
        }

        Assert.Collection(requests, body => Assert.Contains("\"is_urgent\"", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TriageAsync_ReportsAFailureInsteadOfThrowing()
    {
        var (http, jev, _) = CannedJev.Client("this is not json");
        using (http)
        using (jev)
        {
            var summary = await GettingStartedEvaluation.TriageAsync(jev, "Anything.", CancellationToken.None);

            Assert.StartsWith("Jev failed, ", summary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TicketCheck_ExposesProbabilityAndConfidence()
    {
        var check = await CannedJev.EvaluateAsync<TicketCheck>(TicketResponse, "Help!");

        Assert.Equal(0.93, check.IsUrgent.Probability);
        Assert.Equal(SupportTeam.Billing, check.Team.Value);
        Assert.Equal(0.77, check.Team.Confidence);
    }
}
