namespace Minos.Transport;

/// <summary>
/// Reads the provider's request id from an error response's headers: <c>x-request-id</c>, as OpenAI sends it, then
/// <c>x-typesafe-request-id</c>, as TypeSafe sends it, then <c>request-id</c>. Cloudflare's <c>CF-RAY</c> names the CDN
/// hop, not the provider's request, and is never read; nor is a body field such as OpenRouter's <c>id</c>.
/// </summary>
internal static class RequestIdHeader
{
    /// <summary>Gets the header names, in the order they are tried.</summary>
    public static IReadOnlyList<string> Names { get; } = ["x-request-id", "x-typesafe-request-id", "request-id"];

    /// <summary>Returns the first value of the first header in <see cref="Names"/> that is present with a non-blank value.</summary>
    /// <param name="headers">The response headers, by name in any letter case.</param>
    /// <returns>The trimmed request id, or <see langword="null"/> when no such header carries a value.</returns>
    public static string? Read(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        foreach (var name in Names)
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)
                    && header.Value.Count > 0
                    && !string.IsNullOrWhiteSpace(header.Value[0]))
                {
                    return header.Value[0].Trim();
                }
            }
        }

        return null;
    }
}
