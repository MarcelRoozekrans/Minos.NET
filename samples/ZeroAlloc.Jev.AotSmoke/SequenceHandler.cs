using System.Net;
using System.Text;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>Answers with the given statuses in order, repeating the last one, so retries can be observed.</summary>
internal sealed class SequenceHandler(string successBody, params HttpStatusCode[] statuses) : HttpMessageHandler
{
    private int calls;

    public int Calls => calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var status = statuses[Math.Min(calls, statuses.Length - 1)];
        calls++;
        var body = status == HttpStatusCode.OK ? successBody : "{}";
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        });
    }
}
