using System.Net;
using System.Text;

namespace ZeroAlloc.Jev.Docs.Tests;

/// <summary>One canned HTTP reply: a status, a body, headers, and an optional wait before it is sent.</summary>
internal sealed record Reply(HttpStatusCode Status, string Body, IReadOnlyDictionary<string, string>? Headers = null, TimeSpan Delay = default, bool Refuse = false)
{
    public static Reply Ok(string body) => new(HttpStatusCode.OK, body);

    /// <summary>A request that fails as a refused connection would, with no response.</summary>
    public static Reply Refused { get; } = new(HttpStatusCode.ServiceUnavailable, string.Empty, Refuse: true);

    public static Reply Error(int status, string body = "", params (string Name, string Value)[] headers)
        => new((HttpStatusCode)status, body, headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.OrdinalIgnoreCase));
}

/// <summary>What the scripted handler saw in one request.</summary>
internal sealed record Sent(Uri? Uri, string? Authorization, string? RetryCount, string Body, bool HasTraceHeader);

/// <summary>
/// A client whose HTTP replies follow a script, one per request, repeating the last. Reply.Refused makes the
/// request fail as a network error would.
/// </summary>
internal static class ScriptedJev
{
    public static (HttpClient Http, JevClient Jev, IReadOnlyList<Sent> Requests) Client(JevClientOptions options, params Reply[] script)
    {
        var handler = new Handler(script);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://docs.example/api/") };
        JevClient.ConfigureHttpClient(http, options);
        return (http, new JevClient(http, options), handler.Requests);
    }

    /// <summary>Options with a short backoff, so a retried test finishes in milliseconds.</summary>
    public static JevClientOptions Quick(int maxRetries = 2)
        => new()
        {
            ApiKey = "docs-key",
            MaxRetries = maxRetries,
            InitialBackoff = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = TimeSpan.FromMilliseconds(5),
        };

    internal sealed class Handler(Reply[] script) : HttpMessageHandler
    {
        private readonly List<Sent> _requests = [];
        private int _next;

        public IReadOnlyList<Sent> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(new Sent(
                request.RequestUri,
                request.Headers.TryGetValues("Authorization", out var auth) ? string.Join(',', auth) : null,
                request.Headers.TryGetValues("X-TypeSafe-Retry-Count", out var count) ? string.Join(',', count) : null,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Contains("X-Trace")));
            var reply = script[Math.Min(_next++, script.Length - 1)];
            if (reply.Delay > TimeSpan.Zero)
            {
                await Task.Delay(reply.Delay, cancellationToken);
            }

            if (reply.Refuse)
            {
                throw new HttpRequestException("The connection was refused.");
            }

            var response = new HttpResponseMessage(reply.Status)
            {
                Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
            };
            foreach (var (name, value) in reply.Headers ?? new Dictionary<string, string>())
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
    }
}
