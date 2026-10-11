using Minos.Transport;

namespace Minos.OpenAI.Transport;

/// <summary>
/// <see cref="OpenAIDecisionClientOptions"/> resolved against the environment. A class rather than a record so the API key
/// never appears in a generated <c>ToString</c>.
/// </summary>
internal sealed class OpenAIClientSettings
{
    /// <summary>
    /// The path every request posts to, relative to <see cref="BaseAddress"/>, the versioned API root. Relative, so a
    /// base such as <c>https://proxy.example/openai/v1/</c> keeps its path.
    /// </summary>
    public const string Path = "decisions";

    /// <summary>The provider's name in capability and unsupported-request messages.</summary>
    public const string ProviderLabel = "OpenAI";

    /// <summary>The provider's telemetry and metadata name.</summary>
    public const string ProviderName = "openai";

    // Every question kind, no question or option limit, images, and no model listing.
    private static readonly CapabilitySet Defaults = new(QuestionKinds.All, null, null, Images: true, ModelListing: false);

    private OpenAIClientSettings(
        string apiKey,
        Uri baseAddress,
        string model,
        CapabilitySet capabilities,
        OpenAIDecisionClientOptions options)
    {
        ApiKey = apiKey;
        Authorization = "Bearer " + apiKey;
        BaseAddress = baseAddress;
        Model = model;
        Capabilities = capabilities;
        Timeout = options.Timeout;
        MaxRetries = options.MaxRetries;
        InitialBackoff = options.InitialBackoff;
        MaxRetryDelay = options.MaxRetryDelay;
        Jitter = options.Jitter;
        UseStandardPipeline = options.UseStandardPipeline;
    }

    /// <summary>Gets the API key, trimmed.</summary>
    public string ApiKey { get; }

    /// <summary>Gets the <c>Authorization</c> header value, <c>Bearer</c> and the key.</summary>
    /// <remarks>Built once here, so no call allocates it.</remarks>
    public string Authorization { get; }

    /// <summary>Gets the versioned API root, ending in '/'.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets the resolved default model: the option, trimmed, or <see cref="OpenAIDefaults.Model"/>.</summary>
    public string Model { get; }

    /// <summary>Gets OpenAI's capabilities with the options' overrides applied.</summary>
    public CapabilitySet Capabilities { get; }

    public TimeSpan Timeout { get; }

    public int MaxRetries { get; }

    public TimeSpan InitialBackoff { get; }

    public TimeSpan MaxRetryDelay { get; }

    public bool Jitter { get; }

    public bool UseStandardPipeline { get; }

    /// <summary>Resolves options: explicit values first, then environment variables, then defaults.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public static OpenAIClientSettings Resolve(OpenAIDecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new OpenAIDecisionClientOptions();

        ClientSettingRules.CheckTimeout(options.Timeout, nameof(options));
        RetrySettings.Check(options.MaxRetries, options.InitialBackoff, options.MaxRetryDelay, nameof(options));

        var model = options.Model is { } explicitModel ? ClientSettingRules.CheckModel(explicitModel, nameof(options)) : OpenAIDefaults.Model;
        var capabilities = ClientSettingRules.ResolveCapabilities(Defaults, options.Capabilities, nameof(options));
        var apiKey = ResolveApiKey(options, environment);
        var baseAddress = ClientSettingRules.WithTrailingSlash(ResolveRoot(options, environment));

        return new OpenAIClientSettings(apiKey, baseAddress, model, capabilities, options);
    }

    /// <summary>
    /// Resolves only what an <see cref="HttpClient"/> needs: the base address and the per-attempt time-out. Runs the
    /// same checks as <see cref="Resolve"/> on those, with the same messages, and needs no API key.
    /// </summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">The time-out or the base address is invalid.</exception>
    /// <exception cref="InvalidOperationException">The base address environment variable is invalid.</exception>
    public static HttpSettings ResolveHttp(OpenAIDecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new OpenAIDecisionClientOptions();

        ClientSettingRules.CheckTimeout(options.Timeout, nameof(options));

        return new HttpSettings(ClientSettingRules.WithTrailingSlash(ResolveRoot(options, environment)), options.Timeout);
    }

    public override string ToString() => "Provider=OpenAI, BaseAddress=" + BaseAddress.AbsoluteUri + ", ApiKey=***";

    private static string ResolveApiKey(OpenAIDecisionClientOptions options, Func<string, string?> environment)
    {
        if (ClientSettingRules.NonBlank(options.ApiKey) is { } explicitKey)
        {
            return ClientSettingRules.CheckApiKey(explicitKey, nameof(options));
        }

        if (ClientSettingRules.NonBlank(environment(OpenAIDefaults.ApiKeyEnvironmentVariable)) is { } environmentKey)
        {
            return ClientSettingRules.CheckEnvironmentApiKey(environmentKey, OpenAIDefaults.ApiKeyEnvironmentVariable);
        }

        throw new InvalidOperationException(
            "An OpenAI API key is required: set OpenAIDecisionClientOptions.ApiKey or the "
            + OpenAIDefaults.ApiKeyEnvironmentVariable + " environment variable.");
    }

    private static Uri ResolveRoot(OpenAIDecisionClientOptions options, Func<string, string?> environment)
        => options.BaseAddress is { } explicitAddress
            ? ClientSettingRules.CheckBaseAddress(explicitAddress, nameof(options))
            : ClientSettingRules.BaseAddressFromEnvironment(
                environment(OpenAIDefaults.BaseAddressEnvironmentVariable), OpenAIDefaults.BaseAddressEnvironmentVariable)
                ?? OpenAIDefaults.BaseAddress;
}
