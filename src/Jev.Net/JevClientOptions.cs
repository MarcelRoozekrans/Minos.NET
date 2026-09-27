namespace Jev.Net;

/// <summary>Configures a <c>JevClient</c>. Every property is optional.</summary>
public sealed class JevClientOptions
{
    /// <summary>Gets where requests go. Defaults to <see cref="JevProvider.TypeSafe"/>.</summary>
    public JevProvider Provider { get; init; } = JevProvider.TypeSafe;

    /// <summary>
    /// Gets the API key. When unset, the client reads <see cref="JevDefaults.ApiKeyEnvironmentVariable"/> for TypeSafe or
    /// <see cref="JevDefaults.OpenRouterApiKeyEnvironmentVariable"/> for OpenRouter.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets the API root, for a proxy or a test server. When unset, the client reads
    /// <see cref="JevDefaults.BaseAddressEnvironmentVariable"/>, then uses the provider's address.
    /// </summary>
    public Uri? BaseAddress { get; init; }

    /// <summary>Gets how long a request may take. Defaults to 60 seconds. Applies only to an <see cref="HttpClient"/> the client creates.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
}
