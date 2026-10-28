namespace Minos;

/// <summary>What a client says about where its calls go. <see cref="IDecisionClient.GetService"/> returns it; the telemetry stage reads it.</summary>
/// <param name="ProviderName">The provider's telemetry name, such as <c>typesafe</c> or <c>openrouter</c>.</param>
/// <param name="Endpoint">The address calls go to, if there is one: the base address plus the path the client posts to.</param>
/// <param name="DefaultModel">The model used when a request names none, if there is one.</param>
public sealed record DecisionClientMetadata(string ProviderName, Uri? Endpoint, string? DefaultModel)
{
    /// <summary>
    /// Gets an optional value that keeps this client's cache entries apart from other clients with the same provider,
    /// endpoint and model. <see langword="null"/> for the built-in HTTP clients.
    /// </summary>
    /// <value>The text the caching stage adds to each key; <see langword="null"/> adds nothing.</value>
    public string? CacheScope { get; init; }
}
