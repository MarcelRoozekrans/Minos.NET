using System.Net;
using System.Text;

namespace Minos.Samples;

/// <summary>Answers each request from the recordings, by the hash of its body; never touches the network.</summary>
public sealed class ReplayHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _responses;
    private readonly string _sampleName;
    private readonly string _recordCommand;

    /// <param name="provider">The provider these recordings belong to, for a sample that has several; its record command names it.</param>
    public ReplayHandler(RecordingsFile recordings, string sampleName, string? provider = null)
    {
        ArgumentNullException.ThrowIfNull(recordings);
        _sampleName = sampleName;
        _recordCommand = SampleHost.RecordCommand(sampleName, provider);
        _responses = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in recordings.Entries)
        {
            if (!_responses.TryAdd(entry.RequestHash, entry.ResponseBody.GetRawText()))
            {
                throw new InvalidOperationException(
                    "The recordings of " + sampleName + " hold the request hash " + entry.RequestHash + " more than once. "
                    + "Remove the duplicate, or re-record with: " + _recordCommand);
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!_responses.TryGetValue(RequestHash.Of(body), out var response))
        {
            throw new InvalidOperationException(
                "No recorded answer for this request in " + _sampleName + ". Its questions, its data or the client's request "
                + "format changed since it was recorded. Re-record with: " + _recordCommand);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
