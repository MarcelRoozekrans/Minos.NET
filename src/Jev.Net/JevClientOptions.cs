namespace Jev.Net;

/// <summary>Configures a <see cref="JevClient"/>. Every property is optional.</summary>
public sealed class JevClientOptions
{
    /// <summary>Gets or sets where requests go. Defaults to <see cref="JevProvider.TypeSafe"/>.</summary>
    public JevProvider Provider { get; set; } = JevProvider.TypeSafe;

    /// <summary>
    /// Gets or sets the API key. When unset, the client reads <see cref="JevDefaults.ApiKeyEnvironmentVariable"/> for
    /// TypeSafe or <see cref="JevDefaults.OpenRouterApiKeyEnvironmentVariable"/> for OpenRouter.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the API root, for a proxy or a test server. When unset, the client reads
    /// <see cref="JevDefaults.BaseAddressEnvironmentVariable"/> for <see cref="JevProvider.TypeSafe"/> only, then uses
    /// the provider's default address. <see cref="JevDefaults.BaseAddressEnvironmentVariable"/> never applies to
    /// <see cref="JevProvider.OpenRouter"/>, so an OpenRouter key is never sent to a TypeSafe proxy.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>Gets or sets how long a request may take. Defaults to 60 seconds. Applies only to an <see cref="HttpClient"/> the client creates.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
