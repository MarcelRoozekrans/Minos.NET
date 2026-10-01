using System.Diagnostics;
using System.Net;
using System.Text;

namespace ZeroAlloc.Jev.Tests;

/// <summary>No span or metric carries the state, instructions, answers, API key or an error body.</summary>
[Collection(TelemetryListeners.Name)]
public sealed class TelemetryPrivacyTests : IDisposable
{
    private const string State = "STATE-7f3a-secret";
    private const string Instructions = "INSTRUCTION-91c2-secret";
    private const string Answer = "ANSWER-b44f-secret";
    private const string ApiKey = "KEY-5d8e-secret";
    private const string ErrorBody = "BODY-e6a1-secret";

    private static readonly string[] Secrets = [State, Instructions, Answer, ApiKey, ErrorBody];

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task SuccessfulRawEvaluation_LeaksNothing()
    {
        using var capture = new TelemetryCapture();
        var response = $$"""{"model":"jev-1.13.0","answers":{"q":{"type":"choice","choice":"{{Answer}}","probabilities":{"{{Answer}}":1.0},"confidence":0.9} },"usage":{"input_tokens":1,"output_tokens":1} }""";
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, response));

        var result = await client.EvaluateAsync(Request());

        Assert.True(result.IsSuccess);
        AssertNoSecret(capture);
    }

    [Fact]
    public async Task ServerErrorBody_LeaksNothing()
    {
        using var capture = new TelemetryCapture();
        using var client = Client(StubHandler.Json(HttpStatusCode.UnprocessableEntity, $$"""{"detail":"{{ErrorBody}} {{State}}"}"""));

        var result = await client.EvaluateAsync(Request());

        Assert.NotNull(result.Error.Detail);
        AssertNoSecret(capture);
    }

    [Fact]
    public async Task TypedAnswerTheSetRejects_LeaksNothing()
    {
        using var capture = new TelemetryCapture();
        var response = $$"""{"model":"jev-1.13.0","answers":{"department":{"type":"choice","choice":"{{Answer}}","probabilities":{"{{Answer}}":1.0},"confidence":0.9} } }""";
        using var client = Client(StubHandler.Json(HttpStatusCode.OK, response));

        var result = await client.EvaluateAsync<DepartmentRouting>(State);

        // The error message quotes the rejected answer; the span carries only its kind.
        Assert.Equal(JevErrorKind.InvalidResponse, result.Error.Kind);
        AssertNoSecret(capture);
    }

    [Fact]
    public async Task Cancellation_LeaksNothing()
    {
        using var capture = new TelemetryCapture();
        using var client = Client(new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.EvaluateAsync(Request(), cancel.Token));

        Assert.Equal(ActivityStatusCode.Error, capture.Span().Status);
        AssertNoSecret(capture);
    }

    private JevClient Client(StubHandler handler)
    {
        var http = new HttpClient(handler);
        _httpClients.Add(http);
        return new JevClient(http, new JevClientOptions { ApiKey = ApiKey, MaxRetries = 0 });
    }

    private static SystemOneRequest Request() => new()
    {
        State = State,
        Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["q"] = new NoulQuestion { Instructions = Instructions },
        },
    };

    // Every string the listeners saw: span names, statuses, start and end tags, and every metric point's tags.
    private static void AssertNoSecret(TelemetryCapture capture)
    {
        var seen = new StringBuilder();
        foreach (var span in capture.Spans("ZeroAlloc.Jev"))
        {
            seen.AppendLine(span.DisplayName).AppendLine(span.OperationName).AppendLine(span.StatusDescription);
            foreach (var tag in span.TagObjects)
            {
                seen.Append(tag.Key).Append('=').AppendLine(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture));
            }

            foreach (var spanEvent in span.Events)
            {
                seen.AppendLine(spanEvent.Name);
                foreach (var tag in spanEvent.Tags)
                {
                    seen.Append(tag.Key).Append('=').AppendLine(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        foreach (var tag in capture.StartTags())
        {
            seen.Append(tag.Key).Append('=').AppendLine(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var point in capture.AllPoints)
        {
            foreach (var tag in point.Tags)
            {
                seen.Append(tag.Key).Append('=').AppendLine(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // Non-vacuity: the listeners saw the request model start tag and at least one metric point, so the text below is what they recorded.
        Assert.NotEmpty(capture.AllPoints);
        var text = seen.ToString();
        Assert.Contains("gen_ai.request.model=", text, StringComparison.Ordinal);
        foreach (var secret in Secrets)
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }
}
