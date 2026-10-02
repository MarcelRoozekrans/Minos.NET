namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class FanOutTests
{
    private static readonly string[] QuestionKeys = ["category", "bug_severity", "has_repro_steps", "asks_for_refund", "mood"];

    // A blocking bug with repro steps from an angry customer who does not ask for a refund.
    private const string BlockingBugResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "category": { "type": "choice", "choice": "bug_report", "probabilities": { "bug_report": 0.93, "billing": 0.02, "feature_request": 0.03, "account": 0.02 }, "confidence": 0.9 },
            "bug_severity": { "type": "score", "score": 1.8, "legend": { "0": "Cosmetic: nothing stops working", "1": "Degraded: it works, but badly", "2": "Blocking: the customer cannot work" }, "probabilities": { "0": 0.05, "1": 0.1, "2": 0.85 }, "confidence": 0.82 },
            "has_repro_steps": { "type": "noul", "noul": 0.88 },
            "asks_for_refund": { "type": "noul", "noul": 0.1 },
            "mood": { "type": "score", "score": 1.7, "legend": { "0": "Calm", "1": "Annoyed", "2": "Angry" }, "probabilities": { "0": 0.05, "1": 0.2, "2": 0.75 }, "confidence": 0.7 }
          },
          "usage": { "input_tokens": 420, "output_tokens": 40 }
        }
        """;

    // A calm refund request: severity and repro steps are answered, but never read for billing.
    private const string RefundResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "category": { "type": "choice", "choice": "billing", "probabilities": { "bug_report": 0.05, "billing": 0.9, "feature_request": 0.02, "account": 0.03 }, "confidence": 0.86 },
            "bug_severity": { "type": "score", "score": 1.9, "legend": { "0": "Cosmetic: nothing stops working", "1": "Degraded: it works, but badly", "2": "Blocking: the customer cannot work" }, "probabilities": { "0": 0.0, "1": 0.1, "2": 0.9 }, "confidence": 0.8 },
            "has_repro_steps": { "type": "noul", "noul": 0.9 },
            "asks_for_refund": { "type": "noul", "noul": 0.94 },
            "mood": { "type": "score", "score": 0.3, "legend": { "0": "Calm", "1": "Annoyed", "2": "Angry" }, "probabilities": { "0": 0.75, "1": 0.2, "2": 0.05 }, "confidence": 0.7 }
          },
          "usage": { "input_tokens": 410, "output_tokens": 40 }
        }
        """;

    [Fact]
    public async Task BlockingBug_GoesToEngineering_AndIsPrioritized()
    {
        var ticket = await CannedJev.EvaluateAsync<SupportTicket>(BlockingBugResponse, "Export crashes every time. Steps: ...");

        Assert.Equal(TicketActions.EscalateToEngineering | TicketActions.Prioritize, TicketRouting.Route(ticket));
    }

    [Fact]
    public async Task RefundRequest_IgnoresTheBugAnswers()
    {
        var ticket = await CannedJev.EvaluateAsync<SupportTicket>(RefundResponse, "Please refund last month.");

        // A high severity score is present but unread, because the ticket is not a bug report.
        Assert.Equal(TicketActions.FlagForBilling, TicketRouting.Route(ticket));
    }

    [Fact]
    public async Task TriageAsync_RoutesThroughTheClient_InOneRequest()
    {
        var (http, jev, requests) = CannedJev.Client(BlockingBugResponse);
        using (http)
        using (jev)
        {
            Assert.Equal(
                TicketActions.EscalateToEngineering | TicketActions.Prioritize,
                await TicketTriage.TriageAsync(jev, "Export crashes every time.", CancellationToken.None));
        }

        // The guide's core claim: every question travels in the same request.
        Assert.Collection(requests, body =>
        {
            foreach (var key in QuestionKeys)
            {
                Assert.Contains($"\"{key}\"", body, StringComparison.Ordinal);
            }
        });
    }
}
