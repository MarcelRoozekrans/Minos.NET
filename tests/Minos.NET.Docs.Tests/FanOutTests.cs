using System.Text.Json;

namespace Minos.Docs.Tests;

public sealed class FanOutTests
{
    private static readonly string[] QuestionKeys = ["topic", "sentiment", "mentions_data_loss", "names_competitor", "would_recommend"];

    // A very unhappy crash report: the reviewer lost their notes and says they are switching to a named rival.
    // Sentiment: 0 × 0.5 + 1 × 0.4 + 2 × 0.1 = 0.6.
    private const string CrashResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "topic": { "type": "choice", "choice": "crash", "probabilities": { "crash": 0.88, "performance": 0.07, "pricing": 0.02, "feature_idea": 0.02, "praise": 0.01 }, "confidence": 0.86 },
            "sentiment": { "type": "score", "score": 0.6, "legend": { "0": "Very negative", "1": "Negative", "2": "Mixed or neutral", "3": "Positive", "4": "Very positive" }, "probabilities": { "0": 0.5, "1": 0.4, "2": 0.1, "3": 0.0, "4": 0.0 }, "confidence": 0.78 },
            "mentions_data_loss": { "type": "noul", "noul": 0.85 },
            "names_competitor": { "type": "noul", "noul": 0.8 },
            "would_recommend": { "type": "noul", "noul": 0.05 }
          },
          "usage": { "input_tokens": 380, "output_tokens": 40 }
        }
        """;

    // A complaint about a price rise that also mentions a lost saved game: the data-loss answer is high, but it is
    // only read for crash reports, so nobody is paged. Sentiment: 0 × 0.2 + 1 × 0.6 + 2 × 0.2 = 1.0.
    private const string PricingResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "topic": { "type": "choice", "choice": "pricing", "probabilities": { "crash": 0.04, "performance": 0.02, "pricing": 0.86, "feature_idea": 0.05, "praise": 0.03 }, "confidence": 0.82 },
            "sentiment": { "type": "score", "score": 1.0, "legend": { "0": "Very negative", "1": "Negative", "2": "Mixed or neutral", "3": "Positive", "4": "Very positive" }, "probabilities": { "0": 0.2, "1": 0.6, "2": 0.2, "3": 0.0, "4": 0.0 }, "confidence": 0.74 },
            "mentions_data_loss": { "type": "noul", "noul": 0.9 },
            "names_competitor": { "type": "noul", "noul": 0.1 },
            "would_recommend": { "type": "noul", "noul": 0.4 }
          },
          "usage": { "input_tokens": 360, "output_tokens": 40 }
        }
        """;

    [Fact]
    public async Task CrashWithDataLoss_PagesOnCall_AndReachesCompetitiveResearch()
    {
        var review = await CannedDecision.EvaluateAsync<AppReview>(CrashResponse, "Crashed on launch and wiped my notes. Moving to NoteRival.");

        Assert.Equal(ReviewActions.PageOnCall | ReviewActions.ToCompetitiveResearch, ReviewRouting.Route(review));
    }

    [Fact]
    public async Task PricingReview_IgnoresTheDataLossAnswer()
    {
        var review = await CannedDecision.EvaluateAsync<AppReview>(PricingResponse, "Price doubled overnight, and I lost my save when I cancelled.");

        // The data-loss answer is present and high, but unread: the review is about pricing, not a crash.
        Assert.True(review.MentionsDataLoss.Probability > 0.6);
        Assert.Equal(ReviewActions.ToProductTeam, ReviewRouting.Route(review));
    }

    [Fact]
    public async Task TriageAsync_RoutesThroughTheClient_InOneRequest()
    {
        var (http, client, requests) = CannedDecision.Client(CrashResponse);
        using (http)
        using (client)
        {
            Assert.Equal(
                ReviewActions.PageOnCall | ReviewActions.ToCompetitiveResearch,
                await ReviewTriage.TriageAsync(client, "Crashed on launch and wiped my notes.", CancellationToken.None));
        }

        // The guide's core claim: every question travels in the same request.
        Assert.Collection(requests, body => Assert.Equal(QuestionKeys, QuestionKeysOf(body)));
    }

    [Theory]
    [InlineData(nameof(CrashResponse), ReviewActions.PageOnCall | ReviewActions.ToCompetitiveResearch)]
    [InlineData(nameof(PricingResponse), ReviewActions.ToProductTeam)]
    public async Task BuiltQuestions_SendTheSameQuestions_AndRouteTheSame(string responseName, ReviewActions expected)
    {
        var response = string.Equals(responseName, nameof(CrashResponse), StringComparison.Ordinal) ? CrashResponse : PricingResponse;

        var (typedHttp, typedDecision, typedRequests) = CannedDecision.Client(response);
        using (typedHttp)
        using (typedDecision)
        {
            Assert.Equal(expected, await ReviewTriage.TriageAsync(typedDecision, "A review.", CancellationToken.None));
        }

        var (http, client, requests) = CannedDecision.Client(response);
        using (http)
        using (client)
        {
            Assert.Equal(expected, await new BuiltReviewTriage().TriageAsync(client, "A review.", CancellationToken.None));
        }

        // The same five keys as the [Questions] type, and the same questions under them, options and levels included.
        Assert.Collection(requests, body =>
        {
            Assert.Equal(QuestionKeys, QuestionKeysOf(body));
            Assert.Equal(QuestionsOf(typedRequests[0]), QuestionsOf(body));
        });
    }

    private static string QuestionsOf(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        return document.RootElement.GetProperty("questions").GetRawText();
    }

    private static string[] QuestionKeysOf(string requestBody)
    {
        using var document = JsonDocument.Parse(requestBody);
        return [.. document.RootElement.GetProperty("questions").EnumerateObject().Select(question => question.Name)];
    }
}
