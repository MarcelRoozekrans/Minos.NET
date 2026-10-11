namespace Minos.Transport;

/// <summary>The part of a client's settings an <see cref="HttpClient"/> needs, resolved without an API key.</summary>
/// <param name="BaseAddress">The API root, ending in '/'.</param>
/// <param name="Timeout">The per-attempt time-out.</param>
internal readonly record struct HttpSettings(Uri BaseAddress, TimeSpan Timeout);
