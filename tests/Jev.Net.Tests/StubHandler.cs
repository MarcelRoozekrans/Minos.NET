using System.Net;
using System.Text;

namespace Jev.Net.Tests;

/// <summary>An in-memory HTTP handler that records each request and answers with a canned response.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<Captured> Requests { get; } = [];

    public bool Disposed { get; private set; }

    public static StubHandler Json(HttpStatusCode status, string body, string mediaType = "application/json")
        => new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) }));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        Requests.Add(new Captured(
            request.Method,
            request.RequestUri,
            request.Headers.TryGetValues("Authorization", out var auth) ? string.Join(",", auth) : null,
            request.Headers.UserAgent.ToString(),
            body));
        return await respond(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    internal sealed record Captured(HttpMethod Method, Uri? Uri, string? Authorization, string UserAgent, string? Body);
}
