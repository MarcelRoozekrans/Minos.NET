using System.Net;
using System.Text;

namespace ZeroAlloc.Jev.Samples;

/// <summary>Answers each request from the recordings, by the hash of its body; never touches the network.</summary>
public sealed class ReplayHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _responses;
    private readonly string _sampleName;

    public ReplayHandler(RecordingsFile recordings, string sampleName)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        _responses = recordings.Entries.ToDictionary(e => e.RequestHash, e => e.ResponseBody, StringComparer.Ordinal);
        _sampleName = sampleName;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!_responses.TryGetValue(RequestHash.Of(body), out var response))
        {
            throw new InvalidOperationException(
                "No recorded answer for this request in " + _sampleName + ". Its questions or data changed since it was "
                + "recorded. Re-record with: dotnet run --project samples/" + _sampleName + " -- --record");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
