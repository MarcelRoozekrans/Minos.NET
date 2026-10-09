namespace Minos.Transport;

/// <summary>The part of <see cref="DecisionClientSettings"/> an <see cref="HttpClient"/> needs, resolved without an API key.</summary>
/// <param name="BaseAddress">The API root, ending in '/'.</param>
/// <param name="Timeout">The per-attempt time-out.</param>
internal readonly record struct DecisionHttpSettings(Uri BaseAddress, TimeSpan Timeout);
