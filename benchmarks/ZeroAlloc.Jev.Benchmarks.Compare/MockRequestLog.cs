using System.Text.Json;

namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>Reads and clears the mock's request log through WireMock's admin API, <c>/__admin/requests</c>.</summary>
public sealed class MockRequestLog : IDisposable
{
    private static readonly Uri RequestsPath = new("__admin/requests", UriKind.Relative);

    private readonly HttpClient _http;

    /// <summary>Initializes a new instance of the <see cref="MockRequestLog"/> class.</summary>
    /// <param name="baseAddress">The mock's root address.</param>
    public MockRequestLog(Uri baseAddress)
    {
        _http = new HttpClient { BaseAddress = Adapters.ClientAdapters.WithTrailingSlash(baseAddress) };
    }

    /// <summary>Counts the requests the mock has logged, reading the log as a stream so a full log stays cheap.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of logged requests.</returns>
    public async Task<long> CountAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(RequestsPath, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            long count = 0;
            await foreach (var _ in JsonSerializer.DeserializeAsyncEnumerable(stream, CompareJsonContext.Default.JsonElement, cancellationToken).ConfigureAwait(false))
            {
                count++;
            }

            return count;
        }
    }

    /// <summary>
    /// Clears the log. The mock caps its log, so a count taken across a long run would stop growing at the cap; each
    /// counted run starts from an empty log instead.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the log is empty.</returns>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.DeleteAsync(RequestsPath, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
