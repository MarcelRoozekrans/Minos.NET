using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Minos.Conformance;

/// <summary>
/// Answers each request from a fixture, by the probe id <see cref="ExchangeCapture"/> set on it; never touches the network.
/// On <see cref="ConformanceWire.ChatCompletions"/> a probe has one exchange per question, sent in parallel, so a request
/// is answered by the first unanswered exchange of its probe whose recorded request body equals its own, as parsed JSON.
/// </summary>
internal sealed class FixtureHandler(ConformanceFixture fixture, ConformanceWire wire = ConformanceWire.SystemOne) : HttpMessageHandler
{
    private readonly HashSet<string> _answered = new(StringComparer.Ordinal);
    private readonly HashSet<ConformanceExchange> _served = new(ReferenceEqualityComparer.Instance);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (wire == ConformanceWire.ChatCompletions)
        {
            return MatchAsync(request, cancellationToken);
        }

        // The runner checks the fixture before it runs, so a request always finds its exchange; this guards a misuse.
        if (!request.Options.TryGetValue(ExchangeCapture.ProbeOption, out var probe)
            || fixture.Find(probe) is not { Skipped: null, Status: { } status } exchange)
        {
            throw new InvalidOperationException("The fixture has no exchange for this request's probe.");
        }

        // The fixture holds one exchange per probe, so a second request, a retry, has nothing to be answered from.
        lock (_answered)
        {
            if (!_answered.Add(probe))
            {
                throw new InvalidOperationException("The fixture holds one exchange for probe " + probe + ", but it was sent a second request; replay targets must not retry.");
            }
        }

        return Task.FromResult(Respond(request, exchange, status));
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, ConformanceExchange exchange, int status)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(exchange.ResponseText(), Encoding.UTF8),
            RequestMessage = request,
        };
        response.Content.Headers.ContentType = exchange.ContentType is null ? null : MediaTypeHeaderValue.Parse(exchange.ContentType);
        if (exchange.Headers is { } headers)
        {
            foreach (var header in headers)
            {
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }

    private static bool SameBody(JsonElement? recorded, JsonElement? sent)
        => recorded is { } left && sent is { } right && JsonElement.DeepEquals(left, right);

    // The message names the probe only: the body holds request state, which never goes into an exception message.
    private async Task<HttpResponseMessage> MatchAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(ExchangeCapture.ProbeOption, out var probe))
        {
            throw new InvalidOperationException("The fixture has no exchange for this request's probe.");
        }

        var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var body = Parse(text);
        lock (_served)
        {
            foreach (var exchange in fixture.FindAll(probe))
            {
                if (exchange is { Skipped: null, Status: { } status } && !_served.Contains(exchange) && SameBody(exchange.Request, body))
                {
                    _served.Add(exchange);
                    return Respond(request, exchange, status);
                }
            }
        }

        throw new InvalidOperationException("The fixture holds no unanswered exchange of probe " + probe + " whose request body matches this request's; replay targets must send the recorded requests, once each.");
    }

    private static JsonElement? Parse(string? text)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
