using System.Net;
using System.Text;

namespace ZeroAlloc.Jev.Shared;

/// <summary>
/// Answers every request with one canned response but yields first, so the call genuinely completes asynchronously
/// and the logging wrappers' state machines are boxed. Compiled into the AOT smoke app and, linked, into the benchmarks.
/// </summary>
internal sealed class YieldingHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
