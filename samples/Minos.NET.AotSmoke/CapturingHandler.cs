using System.Net;
using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>
/// Answers every request with one canned response and keeps the body it was sent, so a check can read the
/// <c>questions</c> object that really went over the wire. The allocation gates use <see cref="CannedHandler"/>, which
/// reads no body.
/// </summary>
internal sealed class CapturingHandler(string responseBody) : HttpMessageHandler
{
    private byte[] _requestBody = [];

    /// <summary>
    /// Evaluates <paramref name="set"/> over a handler that answers <paramref name="responseBody"/> and returns the
    /// <c>questions</c> object of the request it received.
    /// </summary>
    public static async Task<JsonDocument> QuestionsSentAsync(QuestionSet set, string responseBody)
    {
        var handler = new CapturingHandler(responseBody);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, Program.Options());
        _ = await client.EvaluateAsync(set, SmokeAnswers.State).ConfigureAwait(false);
        return handler.Questions();
    }

    /// <summary>
    /// Evaluates the generated <typeparamref name="T"/> over a handler that answers <paramref name="responseBody"/> and
    /// returns the <c>questions</c> object of the request it received.
    /// </summary>
    public static async Task<JsonDocument> QuestionsSentAsync<T>(string responseBody)
        where T : IQuestionSet<T>
    {
        var handler = new CapturingHandler(responseBody);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, Program.Options());
        _ = await client.EvaluateAsync<T>(SmokeAnswers.State).ConfigureAwait(false);
        return handler.Questions();
    }

    /// <summary>The <c>questions</c> object of the captured request, as an independent document.</summary>
    public JsonDocument Questions()
    {
        using var body = JsonDocument.Parse(_requestBody);
        return JsonDocument.Parse(body.RootElement.GetProperty("questions").GetRawText());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            _requestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
