using System.Globalization;

namespace Minos.Benchmarks.Compare;

/// <summary>
/// Reads the mock's served-request count from <c>GET /count</c>. The harness never resets it, so a runner can read a
/// running total across harnesses.
/// </summary>
public sealed class MockRequestCounter : IDisposable
{
    private static readonly Uri CountPath = new("count", UriKind.Relative);

    private readonly HttpClient _http;

    /// <summary>Initializes a new instance of the <see cref="MockRequestCounter"/> class.</summary>
    /// <param name="baseAddress">The mock's root address.</param>
    public MockRequestCounter(Uri baseAddress)
    {
        _http = new HttpClient { BaseAddress = Adapters.ClientAdapters.WithTrailingSlash(baseAddress) };
    }

    /// <summary>Reads how many <c>POST /v1/systemone</c> requests the mock has served.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The count.</returns>
    public async Task<long> CountAsync(CancellationToken cancellationToken)
    {
        var text = await _http.GetStringAsync(CountPath, cancellationToken).ConfigureAwait(false);
        return long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
