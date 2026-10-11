using System.Net;
using System.Text;

namespace Minos.AotSmoke;

/// <summary>Answers every request with one canned response and its headers, so the smoke test needs no network.</summary>
internal sealed class CannedHandler(HttpStatusCode status, string body, params (string Name, string Value)[] headers) : HttpMessageHandler
{
    private int calls;

    /// <summary>Gets how many requests reached the handler.</summary>
    public int Calls => Volatile.Read(ref calls);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return Task.FromResult(response);
    }
}
