namespace ZeroAlloc.Jev;

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

    /// <summary>
    /// Gets or sets how many times a failed call is retried after the first attempt. Defaults to 2; 0 turns retries off.
    /// </summary>
    /// <remarks>
    /// Retried: rate limiting (429), overload (503, 529), other server errors (5xx), request time-out (408), network
    /// failures and time-outs. Retrying a time-out, a network failure or a 5xx can process, and bill, a request twice when
    /// the first attempt reached the server; set 0 where that matters more than resilience. Valid range: 0–10.
    /// </remarks>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Gets or sets the first wait between attempts, doubled for each further retry. Defaults to 500 ms.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the longest wait between attempts, for the backoff and for a server's <c>Retry-After</c> alike.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether a random jitter is added to the backoff, so many clients do not retry in
    /// step. A server's <c>Retry-After</c> is honoured exactly. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Jitter { get; set; } = true;
}
