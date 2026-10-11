using System.Net.Http.Headers;
using System.Reflection;

namespace Minos.Transport;

/// <summary>
/// What every protocol package's client does to an <see cref="HttpClient"/>: configures one it creates or one passed to
/// its <c>ConfigureHttpClient</c>, and checks the base address of one it borrows.
/// </summary>
internal static class HttpClientSetup
{
    /// <summary>
    /// Applies the settings: the per-attempt time-out, the base address when there is none, and the User-Agent once.
    /// </summary>
    /// <param name="httpClient">The client to configure. It must not have sent a request yet.</param>
    /// <param name="settings">The base address and the time-out.</param>
    /// <param name="userAgent">The package's User-Agent, from <see cref="CreateUserAgent"/>.</param>
    /// <exception cref="InvalidOperationException"><paramref name="httpClient"/> has already sent a request.</exception>
    public static void Apply(HttpClient httpClient, HttpSettings settings, ProductInfoHeaderValue userAgent)
    {
        // Timeout first: its setter can throw, and nothing else has been changed by then.
        httpClient.Timeout = settings.Timeout;
        httpClient.BaseAddress ??= settings.BaseAddress;
        if (!httpClient.DefaultRequestHeaders.UserAgent.Contains(userAgent))
        {
            httpClient.DefaultRequestHeaders.UserAgent.Add(userAgent);
        }
    }

    /// <summary>
    /// Validates a caller-supplied <see cref="HttpClient.BaseAddress"/> with the same rules as a configured address. The
    /// caller's client is never modified, so unlike a configured address, which gets a trailing slash appended, the path
    /// here must already end in '/'.
    /// </summary>
    /// <param name="httpClient">The borrowed client, whose base address is set.</param>
    /// <exception cref="ArgumentException">The base address breaks a rule.</exception>
    public static void CheckBorrowedBaseAddress(HttpClient httpClient)
    {
        var baseAddress = httpClient.BaseAddress!;

        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("httpClient.BaseAddress must be an absolute URI.", nameof(httpClient));
        }

        if (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("httpClient.BaseAddress must use the http or https scheme.", nameof(httpClient));
        }

        if (baseAddress.Query.Length > 0 || baseAddress.Fragment.Length > 0)
        {
            throw new ArgumentException("httpClient.BaseAddress must not contain a query or fragment.", nameof(httpClient));
        }

        if (!baseAddress.AbsoluteUri.EndsWith('/'))
        {
            throw new ArgumentException("httpClient.BaseAddress must end with '/'.", nameof(httpClient));
        }
    }

    /// <summary>
    /// Creates the <c>Minos.NET/&lt;version&gt;</c> User-Agent from <paramref name="assembly"/>'s informational version,
    /// without build metadata.
    /// </summary>
    /// <param name="assembly">The protocol package's assembly.</param>
    /// <returns>The header value; <c>Minos.NET/0.0.0</c> when the version is missing or not a valid product token.</returns>
    public static ProductInfoHeaderValue CreateUserAgent(Assembly assembly)
    {
        // ProductInfoHeaderValue's constructor throws FormatException for a version that is not a valid product token;
        // a User-Agent header must never prevent client construction, so this falls back to "0.0.0" instead.
        try
        {
            return new ProductInfoHeaderValue("Minos.NET", ClientVersion(assembly));
        }
        catch (FormatException)
        {
            return new ProductInfoHeaderValue("Minos.NET", "0.0.0");
        }
    }

    private static string ClientVersion(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(version))
        {
            return "0.0.0";
        }

        // Strip build metadata (e.g. a source-control commit hash appended after '+' by the build), which is not
        // part of the version a caller would want to see or compare in a User-Agent header.
        var buildMetadata = version.IndexOf('+', StringComparison.Ordinal);
        return buildMetadata < 0 ? version : version[..buildMetadata];
    }
}
