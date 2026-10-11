using Minos.OpenAI.Transport;

namespace Minos.OpenAI;

/// <summary>Configures an <see cref="OpenAIDecisionClient"/>. Every property is optional.</summary>
/// <remarks>
/// A client reads these options once, when it is constructed. Changing this instance afterwards has no effect on a
/// client already built.
/// </remarks>
public sealed class OpenAIDecisionClientOptions
{
    /// <summary>
    /// Gets or sets the API key, sent as a Bearer token. When unset or blank, the client reads
    /// <see cref="OpenAIDefaults.ApiKeyEnvironmentVariable"/>; a key is required. Surrounding whitespace is trimmed.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the versioned API root, for a proxy or a test server. When unset, the client reads
    /// <see cref="OpenAIDefaults.BaseAddressEnvironmentVariable"/>, then uses <see cref="OpenAIDefaults.BaseAddress"/>,
    /// <c>https://api.openai.com/v1/</c>. As in OpenAI's official SDKs, the root includes the API version: requests go to
    /// <c>decisions</c> under it, so a proxy is set as <c>https://proxy.example/v1</c>. It must be an absolute http or
    /// https URI without a query or fragment; a trailing '/' is added when missing. An <see cref="HttpClient"/> passed to
    /// an <see cref="OpenAIDecisionClient"/> constructor with its own <see cref="HttpClient.BaseAddress"/> wins over this
    /// option; that address must then be the full versioned API root.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Gets or sets the model that requests without their own <see cref="DecisionRequest.Model"/> ask. When unset,
    /// <see cref="OpenAIDefaults.Model"/>. Surrounding whitespace is trimmed; a blank value is rejected.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets what the API accepts, checked before a request is sent. Each property left unset takes OpenAI's
    /// default: every question kind, no question or option limit, images, and no model listing. See
    /// <see cref="DecisionCapabilities"/> for the check. Binds from configuration under <c>Capabilities</c>, with
    /// <see cref="DecisionCapabilities.QuestionKinds"/> as a comma-separated list such as <c>"Noul, Choice"</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="DecisionCapabilities.ModelListing"/> has no effect: the client has no model listing, so it is always
    /// <see langword="false"/>, and <c>GetService&lt;DecisionCapabilities&gt;()</c> reports it so whatever is set here.
    /// </remarks>
    public DecisionCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Gets or sets how long a single attempt may take. Defaults to 60 seconds. Applies to an <see cref="HttpClient"/>
    /// the client creates, and to one configured with
    /// <see cref="OpenAIDecisionClient.ConfigureHttpClient(HttpClient, OpenAIDecisionClientOptions)"/>, as
    /// <c>AddOpenAIDecisionClient</c>'s are; another <see cref="HttpClient"/> you pass keeps its own time-out. With retries,
    /// a call can take up to <c>(MaxRetries + 1) × Timeout</c> plus the waits between attempts. Must be positive or
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, and at most <see cref="int.MaxValue"/> milliseconds,
    /// which is about 24.8 days.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets how many times a failed call is retried after the first attempt. Defaults to 2; 0 turns retries off.
    /// </summary>
    /// <remarks>
    /// Retried: rate limiting (429), overload (503, 529), other server errors (5xx), request time-out (408), network
    /// failures and time-outs. A refusal is not retried. Retrying a time-out, a network failure or a 5xx can process, and
    /// bill, a request twice when the first attempt reached the server; set 0 where that matters more than resilience.
    /// Valid range: 0–10.
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
    public bool UseStandardPipeline { get; set; } = true;

    /// <summary>
    /// Checks these options exactly as an <see cref="OpenAIDecisionClient"/> constructor does, including the API key and
    /// base address environment variables, and throws what the constructor would throw. It creates no client and changes
    /// no option.
    /// </summary>
    /// <remarks>
    /// The constructors run the same check, so options that pass here construct a client, as long as the environment
    /// variables do not change in between. <c>AddOpenAIDecisionClient</c> runs it at host startup.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// An option has an invalid value: an invalid time-out or retry setting; a blank model; an API key with a control
    /// character; a base address that is not an absolute http or https URI, or has a query or fragment; or a
    /// <see cref="Capabilities"/> override that allows no question kind or names an unknown one, a
    /// <see cref="DecisionCapabilities.MaxQuestions"/> below 1, or a <see cref="DecisionCapabilities.MaxChoiceOptions"/>
    /// below 2.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No API key is configured, or the <see cref="OpenAIDefaults.ApiKeyEnvironmentVariable"/> or
    /// <see cref="OpenAIDefaults.BaseAddressEnvironmentVariable"/> environment variable is invalid.
    /// </exception>
    public void Validate() => Validate(Environment.GetEnvironmentVariable);

    // The constructors' own check, against a given environment; tests pass a fake.
    internal void Validate(Func<string, string?> environment) => _ = OpenAIClientSettings.Resolve(this, environment);
}
