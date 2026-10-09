namespace Minos.Docs.Tests;

public sealed class ReadmeTests
{
    private const string Response = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.93 },
            "queue": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.81, "technical": 0.19 }, "confidence": 0.77 }
          },
          "usage": { "input_tokens": 120, "output_tokens": 12 }
        }
        """;

    [Fact]
    public async Task RouteAsync_ReadsBothAnswers()
    {
        var (http, client, requests) = CannedDecision.Client(Response);
        using (http)
        using (client)
        {
            var summary = await ReadmeExample.RouteAsync(client, "Help! My payouts have been failing for 3 days.", CancellationToken.None);

            Assert.Equal("urgent: True, queue: Billing", summary);
        }

        Assert.Collection(requests, body => Assert.Contains("\"is_urgent\"", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RouteAsync_ReportsAFailureInsteadOfThrowing()
    {
        var (http, client, _) = CannedDecision.Client("this is not json");
        using (http)
        using (client)
        {
            var summary = await ReadmeExample.RouteAsync(client, "Anything.", CancellationToken.None);

            Assert.StartsWith("InvalidResponse: ", summary, StringComparison.Ordinal);
        }
    }
}
