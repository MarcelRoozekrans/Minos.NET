using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Minos.Conformance;

/// <summary>Answers each request from a fixture, by the probe id <see cref="ExchangeCapture"/> set on it; never touches the network.</summary>
internal sealed class FixtureHandler(ConformanceFixture fixture) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The runner checks the fixture before it runs, so a request always finds its exchange; this guards a misuse.
        if (!request.Options.TryGetValue(ExchangeCapture.ProbeOption, out var probe)
            || fixture.Find(probe) is not { Skipped: null, Status: { } status } exchange)
        {
            throw new InvalidOperationException("The fixture has no exchange for this request's probe.");
        }

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

        return Task.FromResult(response);
    }
}
