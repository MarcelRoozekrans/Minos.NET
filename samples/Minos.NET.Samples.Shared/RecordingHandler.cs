namespace Minos.Samples;

/// <summary>
/// Sends each request on, then records the body of a successful response under the request's hash. It never reads or
/// stores headers, so the API key cannot reach the recordings.
/// </summary>
public sealed class RecordingHandler(RecordingSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Reading the content buffers it, so the bytes hashed here are the bytes sent below.
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            session.Add(RequestHash.Of(body), text);
        }

        return response;
    }
}
