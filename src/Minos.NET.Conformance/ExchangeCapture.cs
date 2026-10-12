namespace Minos.Conformance;

/// <summary>
/// Records each exchange of the probe in progress: the request body, the response status, its media type, its body and
/// an allowlist of headers, the request ids and the two retry-after forms. It never records <c>Authorization</c>,
/// <c>x-api-key</c> or any other header. For the bad-key probe it replaces the key with <see cref="InvalidKey"/>, so no
/// target needs a factory of its own for it.
/// </summary>
public sealed class ExchangeCapture : DelegatingHandler
{
    /// <summary>The key the bad-key probe sends.</summary>
    public const string InvalidKey = "minos-conformance-invalid-key";

    /// <summary>The request option that tells the fixture handler which probe a request belongs to.</summary>
    internal static readonly HttpRequestOptionsKey<string> ProbeOption = new("minos.conformance.probe");

    private static readonly string[] KnownSchemes = ["Bearer", "Basic", "Digest", "Token"];

    private readonly List<ConformanceExchange> _exchanges = [];
    private ConformanceProbe? _probe;

    /// <summary>Initializes a new instance of the <see cref="ExchangeCapture"/> class.</summary>
    /// <param name="innerHandler">The handler that sends the requests: the network, or a fixture.</param>
    public ExchangeCapture(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    /// <summary>Gets the response headers an exchange keeps, by lower-case name.</summary>
    public static IReadOnlyList<string> RecordedHeaders { get; } = [.. ConformanceChecks.RequestIdHeaders, "retry-after", "retry-after-ms"];

    /// <summary>Starts recording a probe's exchanges.</summary>
    /// <param name="probe">The probe whose requests follow.</param>
    public void Begin(ConformanceProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
        _exchanges.Clear();
    }

    /// <summary>Stops recording and returns the probe's exchanges, in the order they were sent.</summary>
    /// <returns>The exchanges.</returns>
    public IReadOnlyList<ConformanceExchange> End()
    {
        var exchanges = _exchanges.ToArray();
        _exchanges.Clear();
        _probe = null;
        return exchanges;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var probe = _probe ?? throw new InvalidOperationException("A request was sent outside a probe; call Begin first.");
        if (probe.Kind == ProbeKind.BadKey)
        {
            SwapKey(request);
        }

        request.Options.Set(ProbeOption, probe.Id);

        // Reading the content buffers it, so the bytes kept here are the bytes sent below.
        var requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _exchanges.Add(ConformanceExchange.FromHttp(
            probe.Id, requestBody, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString(), Allowlisted(response), responseBody));
        return response;
    }

    // Fails closed: the raw header is read, so a key that does not parse as a credential is still replaced, and a header
    // that cannot be replaced stops the probe instead of sending the real key.
    private static void SwapKey(HttpRequestMessage request)
    {
        Replace(request, "Authorization", static raw => KeepScheme(raw));
        Replace(request, "x-api-key", static _ => InvalidKey);
    }

    // Keeps a known credential scheme, such as Bearer or Basic, and swaps everything after it. Any other value becomes the
    // bare key, so a key that happens to contain a space is never partly sent.
    private static string KeepScheme(string raw)
    {
        var trimmed = raw.TrimStart();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (space > 0)
        {
            var scheme = trimmed[..space];
            foreach (var known in KnownSchemes)
            {
                if (string.Equals(scheme, known, StringComparison.OrdinalIgnoreCase))
                {
                    return known + " " + InvalidKey;
                }
            }
        }

        return InvalidKey;
    }

    private static void Replace(HttpRequestMessage request, string name, Func<string, string> swapped)
    {
        if (!request.Headers.TryGetValues(name, out var values))
        {
            return;
        }

        var raw = string.Join(", ", values);
        if (!request.Headers.Remove(name) || !request.Headers.TryAddWithoutValidation(name, swapped(raw)))
        {
            throw new InvalidOperationException("The bad-key probe could not replace the " + name + " header, so it was not sent.");
        }
    }

    private static Dictionary<string, string>? Allowlisted(HttpResponseMessage response)
    {
        Dictionary<string, string>? kept = null;
        foreach (var name in RecordedHeaders)
        {
            if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
            {
                (kept ??= new Dictionary<string, string>(StringComparer.Ordinal))[name] = string.Join(", ", values);
            }
        }

        return kept;
    }
}
