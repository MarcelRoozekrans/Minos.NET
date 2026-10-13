using Minos.SystemOne.Transport;

namespace Minos.SystemOne;

/// <summary>Configures a <see cref="SystemOneDecisionClient"/>. Every property is optional.</summary>
/// <remarks>
/// A client reads these options once, when it is constructed. Changing this instance afterwards has no effect on a
/// client already built.
/// </remarks>
public sealed class SystemOneDecisionClientOptions
{
    /// <summary>
    /// Gets or sets where requests go. Defaults to <see cref="SystemOneProvider.TypeSafe"/>. The provider sets the default
    /// base address, the request path, where the API key comes from and the default model:
    /// <list type="bullet">
    /// <item><description><see cref="SystemOneProvider.TypeSafe"/>: <c>https://api.typesafe.ai/</c>, or
    /// <see cref="SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable"/>; path <c>v1/systemone</c>; a key is
    /// required; model <c>jev-latest</c>.</description></item>
    /// <item><description><see cref="SystemOneProvider.OpenRouter"/>: <c>https://openrouter.ai/api/</c>; path
    /// <c>v1/systemone</c>; a key is required; model <c>jev-latest</c>.</description></item>
    /// <item><description><see cref="SystemOneProvider.VllmSemanticRouter"/> and <see cref="SystemOneProvider.Custom"/>:
    /// <see cref="BaseAddress"/> is required; path <see cref="Path"/>, or <c>v1/systemone</c>; a key only when
    /// <see cref="ApiKey"/> is set; no default model.</description></item>
    /// </list>
    /// </summary>
    public SystemOneProvider Provider { get; set; } = SystemOneProvider.TypeSafe;

    /// <summary>
    /// Gets or sets the API key, sent as a Bearer token. When unset, the client reads
    /// <see cref="SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable"/> for TypeSafe,
    /// <see cref="SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable"/> for OpenRouter; those providers
    /// require one. <see cref="SystemOneProvider.VllmSemanticRouter"/> and <see cref="SystemOneProvider.Custom"/> never read
    /// a key from the environment, so a vendor key is never sent to a server you name; without this option they send no
    /// <c>Authorization</c> header.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the API root, for a proxy, a test server or a server you run. When unset, the client reads
    /// <see cref="SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable"/> for
    /// <see cref="SystemOneProvider.TypeSafe"/> only, then uses the provider's default address.
    /// <see cref="SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable"/> never applies to another provider, so an OpenRouter key is never sent to a TypeSafe proxy.
    /// <see cref="SystemOneProvider.VllmSemanticRouter"/> and <see cref="SystemOneProvider.Custom"/> have no default address,
    /// so it must be set for them. An <see cref="HttpClient"/> passed to a <see cref="SystemOneDecisionClient"/> constructor with its
    /// own <see cref="HttpClient.BaseAddress"/> wins over this option; that address must then be the full API root.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the model that typed evaluation calls, including <c>EvaluateUtf8Async</c>, and built question sets
    /// ask, as a versioned id such as <c>jev-1.13.0</c> or an alias. When unset, the preset's default: jev-latest for
    /// TypeSafe and OpenRouter. The other providers have none, so it must be set.
    /// Surrounding whitespace is trimmed; a blank value is rejected.
    /// A <see cref="SystemOneRequest"/> that names no model of its own is sent with this one.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the request path, relative to <see cref="BaseAddress"/>, for <see cref="SystemOneProvider.Custom"/> and
    /// <see cref="SystemOneProvider.VllmSemanticRouter"/> only. When unset, <c>v1/systemone</c>. It must be relative, must not
    /// start with <c>/</c>, and must not contain a query, a fragment or a <c>..</c> segment. Surrounding whitespace is
    /// trimmed, and a blank value counts as unset. Setting it for a hosted provider is rejected.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets what the provider accepts, checked before a request is sent. Each property left unset takes the
    /// <see cref="Provider"/> preset's default; see <see cref="DecisionCapabilities"/> for the check.
    /// Binds from configuration under <c>Capabilities</c>, with <see cref="DecisionCapabilities.QuestionKinds"/> as a
    /// comma-separated list such as <c>"Noul, Choice"</c>.
    /// </summary>
    /// <remarks>
    /// <para>The preset defaults:</para>
    /// <list type="bullet">
    /// <item><description><see cref="SystemOneProvider.TypeSafe"/>: every kind, no question limit, 255 Choice options, no
    /// images, model listing.</description></item>
    /// <item><description><see cref="SystemOneProvider.OpenRouter"/>: every kind, no question limit, 255 Choice options, no
    /// images, no model listing.</description></item>
    /// <item><description><see cref="SystemOneProvider.VllmSemanticRouter"/>: every kind, no question or option limit, no
    /// images, no model listing.</description></item>
    /// <item><description><see cref="SystemOneProvider.Custom"/>: every kind, no question or option limit, no images, no
    /// model listing, because a self-hosted server's <c>GET v1/models</c> need not use TypeSafe's shape.</description></item>
    /// </list>
    /// <para>
    /// The raw <see cref="SystemOneDecisionClient.EvaluateAsync(SystemOneRequest, CancellationToken)"/> call is not checked.
    /// <see cref="DecisionCapabilities.SupportsModelListing"/> gates <see cref="SystemOneDecisionClient.ListModelsAsync(CancellationToken)"/>.
    /// </para>
    /// </remarks>
    public DecisionCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets how long a single attempt may take. Defaults to 60 seconds. Applies to an <see cref="HttpClient"/>
    /// the client creates, and to one configured with <see cref="SystemOneDecisionClient.ConfigureHttpClient(HttpClient, SystemOneDecisionClientOptions)"/>,
    /// as <c>AddSystemOneDecisionClient</c>'s are; another <see cref="HttpClient"/> you pass keeps its own time-out. With retries, a call
    /// can take up to <c>(MaxRetries + 1) × Timeout</c> plus the waits between attempts. Must be positive or
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, and at most <see cref="int.MaxValue"/> milliseconds,
    /// which is about 24.8 days.
    /// </summary>
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

    /// <summary>
    /// Gets or sets the first wait between attempts, doubled for each further retry and capped by
    /// <see cref="MaxRetryDelay"/>. Defaults to 500 ms. Must be positive and at most <see cref="int.MaxValue"/>
    /// milliseconds.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets or sets the longest wait between attempts, for the backoff and for a server's <c>Retry-After</c> alike.
    /// Defaults to 30 seconds. Must be at least <see cref="InitialBackoff"/> and at most <see cref="int.MaxValue"/>
    /// milliseconds.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether a random jitter is added to the backoff, so many clients do not retry in
    /// step. Jitter adds up to 50 % of the backoff delay, and the result never exceeds <see cref="MaxRetryDelay"/>. A
    /// server's <c>Retry-After</c> is honoured exactly, up to <see cref="MaxRetryDelay"/>. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool Jitter { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the client runs its standard pipeline: telemetry, then logging when it has a logger factory,
    /// then retries. <see langword="false"/> leaves the bare transport, one attempt per call, to wrap with
    /// <see cref="DecisionClientBuilder"/>. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// It applies to the raw <see cref="SystemOneDecisionClient.EvaluateAsync(SystemOneRequest, CancellationToken)"/> and
    /// <see cref="SystemOneDecisionClient.ListModelsAsync(CancellationToken)"/> calls too: with <see langword="false"/> they send one
    /// attempt each and are not retried. They keep their own span and log either way.
    /// </remarks>
    public bool UseStandardPipeline { get; set; } = true;

    /// <summary>
    /// Checks these options exactly as a <see cref="SystemOneDecisionClient"/> constructor does, including the API key and base address
    /// environment variables, and throws what the constructor would throw. It creates no client and changes no option.
    /// </summary>
    /// <remarks>
    /// The constructors run the same check, so options that pass here construct a client, as long as the environment
    /// variables do not change in between. <c>AddSystemOneDecisionClient</c> runs it at host startup.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value: an unknown provider; an invalid time-out or retry setting; a blank model, or none for
    /// a provider without a default model; an API key with a control
    /// character; a base address that is not an absolute http or https URI, or has a query or fragment; no base address
    /// for <see cref="SystemOneProvider.VllmSemanticRouter"/> or <see cref="SystemOneProvider.Custom"/>; <see cref="Path"/> set for a hosted provider; or a path that is not
    /// relative, starts with <c>/</c>, or contains a query, a fragment or a <c>..</c> segment; or a
    /// <see cref="Capabilities"/> override that allows no question kind or names an unknown one, a
    /// <see cref="DecisionCapabilities.MaxQuestions"/> below 1, or a <see cref="DecisionCapabilities.MaxChoiceOptions"/>
    /// below 2.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No API key is configured for a provider that requires one, or the
    /// <see cref="SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable"/>,
    /// <see cref="SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable"/>
    /// or <see cref="SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable"/> environment variable is invalid.
    /// </exception>
    public void Validate() => Validate(Environment.GetEnvironmentVariable);

    // The constructors' own check, against a given environment; tests pass a fake.
    internal void Validate(Func<string, string?> environment) => _ = SystemOneDecisionClientSettings.Resolve(this, environment);
}
