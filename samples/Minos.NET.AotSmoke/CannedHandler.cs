using System.Net;
using System.Text;

namespace Minos.AotSmoke;

/// <summary>Answers every request with one canned response, so the smoke test needs no network.</summary>
internal sealed class CannedHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        });
}
