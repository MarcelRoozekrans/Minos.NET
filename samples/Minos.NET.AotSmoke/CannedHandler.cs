using System.Net;
using System.Text;

namespace Minos.AotSmoke;

/// <summary>Answers every request with one canned response, so the smoke test needs no network.</summary>
internal sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    private int calls;

    /// <summary>Gets how many requests reached the handler.</summary>
    public int Calls => Volatile.Read(ref calls);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        });
    }
}
