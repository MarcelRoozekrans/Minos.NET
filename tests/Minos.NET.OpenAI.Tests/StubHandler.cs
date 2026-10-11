using System.Diagnostics;

namespace Minos.OpenAI.Tests;

/// <summary>An in-memory HTTP handler that records each request and answers with a canned response; a copy of the SystemOne tests' one.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<Captured> Requests { get; } = [];

    public bool Disposed { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new Captured(
            request.Method,
            request.RequestUri,
            request.Headers.TryGetValues("Authorization", out var auth) ? string.Join(",", auth) : null,
            request.Headers.UserAgent.ToString(),
            body,
            Stopwatch.GetTimestamp()));
        return await respond(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    internal sealed record Captured(HttpMethod Method, Uri? Uri, string? Authorization, string UserAgent, string? Body, long Timestamp);
}
