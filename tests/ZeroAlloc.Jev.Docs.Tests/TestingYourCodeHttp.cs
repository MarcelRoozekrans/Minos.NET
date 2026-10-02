namespace ZeroAlloc.Jev.Docs.Tests;

#region TestingYourCode_Handler
using System.Net;
using System.Text;

// Answers every request with one canned status and body, and keeps the bodies it was sent.
public sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    private readonly List<string> _requestBodies = [];

    public IReadOnlyList<string> RequestBodies => _requestBodies;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
#endregion

public sealed class TestingYourCodeHttpTests
{
    #region TestingYourCode_Body
    // What Jev sends back. The keys under "answers" are the question keys: the property names in snake_case.
    private const string UrgentBody = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.92 },
            "desk": {
              "type": "choice",
              "choice": "billing",
              "probabilities": { "billing": 0.7, "technical": 0.2, "product_team": 0.1 },
              "confidence": 0.8
            }
          },
          "usage": { "input_tokens": 40, "output_tokens": 6 }
        }
        """;
    #endregion

    #region TestingYourCode_HttpTests
    [Fact]
    public async Task ARealClient_SendsTheQuestions_AndReadsTheCannedAnswers()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, UrgentBody);
        using var http = new HttpClient(handler);
        // A dummy key passes validation, and no retries means a failing reply is returned at once.
        using var jev = new JevClient(http, new JevClientOptions { ApiKey = "test-key", MaxRetries = 0 });

        var route = await new TicketTriager(jev).RouteAsync("Payouts have been failing for 3 days.", CancellationToken.None);

        Assert.Equal(TriageRoute.Escalate, route);
        Assert.Collection(handler.RequestBodies, sent =>
        {
            using var request = System.Text.Json.JsonDocument.Parse(sent);
            Assert.Equal("Payouts have been failing for 3 days.", request.RootElement.GetProperty("state").GetString());
            Assert.True(request.RootElement.GetProperty("questions").TryGetProperty("is_urgent", out _));
        });
    }

    [Fact]
    public async Task ARejectedKey_BecomesAFailure_AndThePersonReviews()
    {
        var handler = new CannedHandler(HttpStatusCode.Unauthorized, """{"error":"Invalid API key"}""");
        using var http = new HttpClient(handler);
        using var jev = new JevClient(http, new JevClientOptions { ApiKey = "test-key", MaxRetries = 0 });

        var result = await jev.EvaluateAsync<TriageQuestions>("Any ticket.", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(JevErrorKind.Unauthorized, result.Error.Kind);
        Assert.Equal(TriageRoute.Review, await new TicketTriager(jev).RouteAsync("Any ticket.", CancellationToken.None));
    }
    #endregion
}
