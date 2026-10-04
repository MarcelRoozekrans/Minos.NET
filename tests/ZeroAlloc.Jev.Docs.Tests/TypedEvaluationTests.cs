using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class TypedEvaluationTests
{
    // Impact: 0 x 0.1 + 1 x 0.2 + 2 x 0.7 = 1.6.
    private const string ReviewResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.91 },
            "desk": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.8, "technical": 0.1, "sales": 0.05, "other": 0.05 }, "confidence": 0.72 },
            "impact": { "type": "score", "score": 1.6, "legend": { "0": "Cosmetic or minor", "1": "Slows the customer down", "2": "Blocks the customer's work" }, "probabilities": { "0": 0.1, "1": 0.2, "2": 0.7 }, "confidence": 0.66 }
          },
          "usage": { "input_tokens": 140, "output_tokens": 18 }
        }
        """;

    private const string UrgencyResponse = """
        {
          "model": "jev-1.13.0",
          "answers": { "is_urgent": { "type": "noul", "noul": 0.9 } },
          "usage": { "input_tokens": 20, "output_tokens": 2 }
        }
        """;

    private static readonly SupportTicket Ticket = new("Payouts failing", "Help! My payouts have been failing for 3 days.", "Pro");

    [Fact]
    public async Task ReviewAsync_SendsTheStateAndReadsTheTypedAnswers()
    {
        var (http, jev, requests) = CannedJev.Client(ReviewResponse);
        using (http)
        using (jev)
        {
            var review = await TicketReviewing.ReviewAsync(jev, Ticket, CancellationToken.None);

            Assert.Equal((true, Desk.Billing, Impact.High), review);
        }

        Assert.Collection(requests, _ => { });
        using var request = JsonDocument.Parse(requests[0]);
        var state = request.RootElement.GetProperty("state");
        Assert.Equal("Payouts failing", state.GetProperty("subject").GetString());
        Assert.Equal("Pro", state.GetProperty("plan").GetString());
        Assert.Equal("jev-latest", request.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task ReviewAsync_ReportsAFailureAsNull()
    {
        var (http, jev, _) = CannedJev.Client("this is not json");
        using (http)
        using (jev)
        {
            Assert.Null(await TicketReviewing.ReviewAsync(jev, Ticket, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ATypedAnswer_HoldsEveryQuestionsAnswer()
    {
        var review = await CannedJev.EvaluateAsync<TicketReview>(ReviewResponse, "text");

        Assert.Equal(0.91, review.IsUrgent.Probability);
        Assert.Equal(0.72, review.Desk.Confidence);
        Assert.Equal(1.6, review.Impact.Expected);
    }

    [Fact]
    public void TheGeneratedQuestions_UseSnakeCaseKeysAndStructuredCriteria()
    {
        using var actual = JsonDocument.Parse(TicketReview.QuestionsUtf8.ToArray());
        using var expected = JsonDocument.Parse("""
            {
              "is_urgent": {
                "type": "noul",
                "instructions": "Is the `body` urgent, for a customer on the `plan` they have?",
                "criteria": { "true": "The customer needs help right away", "false": "The customer can wait" }
              },
              "desk": {
                "type": "choice",
                "instructions": "Which desk should handle this?",
                "criteria": {
                  "billing": {
                    "description": "Payments, invoices and refunds",
                    "examples": ["I was charged twice"],
                    "not_for": ["How much is the Pro plan?"]
                  },
                  "technical": { "description": "Bugs, outages and integrations", "examples": ["The API returns a 500"] },
                  "sales": "Pricing, upgrades and new accounts",
                  "other": null
                }
              },
              "impact": {
                "type": "score",
                "instructions": "How much does this affect the customer?",
                "criteria": [
                  { "description": "Cosmetic or minor", "not_for": ["Data loss"] },
                  "Slows the customer down",
                  "Blocks the customer's work"
                ]
              }
            }
            """);

        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Fact]
    public void TheWireJsonOnThePage_IsWhatTheGeneratorWrites()
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", "typed-evaluation.md"));
        var start = Array.FindIndex(lines, line => string.Equals(line, "```json", StringComparison.Ordinal));
        Assert.True(start >= 0, "The page has a json block.");
        var end = Array.FindIndex(lines, start + 1, line => string.Equals(line, "```", StringComparison.Ordinal));
        using var onPage = JsonDocument.Parse(string.Join('\n', lines[(start + 1)..end]));
        using var generated = JsonDocument.Parse(TicketReview.QuestionsUtf8.ToArray());

        Assert.True(JsonElement.DeepEquals(onPage.RootElement, generated.RootElement));
    }

    [Fact]
    public async Task TheTStateOverload_RejectsANullStateOrNullMetadata()
    {
        var (http, jev, _) = CannedJev.Client(ReviewResponse);
        using (http)
        using (jev)
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await jev.EvaluateAsync<TicketReview, SupportTicket>(
                null!, SupportTicketJson.Default.SupportTicket, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await jev.EvaluateAsync<TicketReview, SupportTicket>(
                Ticket, null!, CancellationToken.None));
        }
    }

    [Fact]
    public async Task EachStateForm_IsSentInItsOwnShape()
    {
        using var element = JsonDocument.Parse("""{"subject":"Payouts failing"}""");
        var (http, jev, requests) = CannedJev.Client(UrgencyResponse);
        using (http)
        using (jev)
        {
            var succeeded = await TicketReviewing.EvaluateEachFormAsync(
                jev, "Help! My payouts are failing.", element.RootElement, """{"subject":"Payouts failing"}"""u8.ToArray(), CancellationToken.None);

            Assert.Equal(3, succeeded);
        }

        Assert.Equal(3, requests.Count);
        var kinds = new List<JsonValueKind>();
        foreach (var body in requests)
        {
            using var request = JsonDocument.Parse(body);
            kinds.Add(request.RootElement.GetProperty("state").ValueKind);
        }

        Assert.Equal([JsonValueKind.String, JsonValueKind.Object, JsonValueKind.Object], kinds);
    }

    [Fact]
    public async Task ASetWithAState_AlsoTakesTextAsItsState()
    {
        var (http, jev, requests) = CannedJev.Client(ReviewResponse);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync<TicketReview>("Help!", CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        Assert.Collection(requests, _ => { });
        using var request = JsonDocument.Parse(requests[0]);
        Assert.Equal(JsonValueKind.String, request.RootElement.GetProperty("state").ValueKind);
    }

    [Fact]
    public async Task ARejectedState_ThrowsInsteadOfFailing()
    {
        var (http, jev, _) = CannedJev.Client(UrgencyResponse);
        using (http)
        using (jev)
        {
            using var number = JsonDocument.Parse("42");

            await Assert.ThrowsAsync<ArgumentException>(
                async () => await jev.EvaluateAsync<UrgencyCheck>(number.RootElement, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await jev.EvaluateUtf8Async<UrgencyCheck>(Encoding.UTF8.GetBytes("42"), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(
                async () => await jev.EvaluateAsync<UrgencyCheck>((string)null!, CancellationToken.None));
        }
    }

    [Fact]
    public async Task AMissingAnswer_IsAFailureOfKindInvalidResponse()
    {
        var (http, jev, _) = CannedJev.Client(UrgencyResponse);
        using (http)
        using (jev)
        {
            var result = await jev.EvaluateAsync<TicketReview>("Help!", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        }
    }

    [Fact]
    public void JevContent_HoldsTextOrStructuredJson()
    {
        var forms = TicketReviewing.ContentForms(Ticket);

        Assert.True(forms[0].IsString);
        Assert.False(forms[1].IsString);
        Assert.True(forms[1].TryGetJson(out var value));
        Assert.Equal("Pro", value.GetProperty("plan").GetString());
        Assert.False(forms[2].IsString);
        Assert.True(forms[2].TryGetJson(out var parsed));
        Assert.Equal("Payouts failing", parsed.GetProperty("subject").GetString());
    }

    [Fact]
    public void JevContent_AJsonStringBecomesText_AndAnythingElseThrows()
    {
        Assert.True(JevContent.FromUtf8Json("\"plain text\""u8).IsString);
        Assert.Equal("plain text", JevContent.FromUtf8Json("\"plain text\""u8).ToString());

        var number = (JsonTypeInfo<int>)JsonSerializerOptions.Default.GetTypeInfo(typeof(int));
        Assert.Throws<ArgumentException>(() => JevContent.FromValue(42, number));
        Assert.Throws<ArgumentException>(() => JevContent.FromUtf8Json("42"u8));
        Assert.Throws<ArgumentException>(() => JevContent.FromUtf8Json("true"u8));
        Assert.Throws<ArgumentException>(() => JevContent.FromUtf8Json("null"u8));
        Assert.Throws<ArgumentException>(() => JevContent.FromUtf8Json("{} {}"u8));
        Assert.Throws<ArgumentException>(() => JevContent.FromUtf8Json(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => JevContent.FromString(null!));
        Assert.Throws<ArgumentNullException>(() => JevContent.FromValue(Ticket, null!));
    }

    [Fact]
    public void JevContent_TextConvertsImplicitly()
    {
        JevContent text = "Help!";

        Assert.True(text.TryGetString(out var value));
        Assert.Equal("Help!", value);
    }
}
