using Minos.Transport;

namespace Minos.SystemOne.Transport;

/// <summary>
/// <see cref="SystemOneDecisionClientOptions"/> resolved against the environment. A class rather than a record so the API key never
/// appears in a generated <c>ToString</c>.
/// </summary>
internal sealed class SystemOneDecisionClientSettings
{
    /// <summary>The <c>/v1/systemone</c> path every preset posts to, unless <see cref="SystemOneDecisionClientOptions.Path"/> overrides it.</summary>
    private const string DefaultPath = "v1/systemone";

    private SystemOneDecisionClientSettings(
        SystemOneProvider provider,
        string? apiKey,
        Uri baseAddress,
        string path,
        string model,
        CapabilitySet capabilities,
        TimeSpan timeout,
        int maxRetries,
        TimeSpan initialBackoff,
        TimeSpan maxRetryDelay,
        bool jitter,
        bool useStandardPipeline,
        ReadOnlyMemory<byte> providerMember)
    {
        Provider = provider;
        ApiKey = apiKey;
        Authorization = apiKey is null ? null : "Bearer " + apiKey;
        BaseAddress = baseAddress;
        Path = path;
        Model = model;
        Capabilities = capabilities;
        ProviderName = SystemOneTelemetry.ProviderOf(provider);
        Timeout = timeout;
        MaxRetries = maxRetries;
        InitialBackoff = initialBackoff;
        MaxRetryDelay = maxRetryDelay;
        Jitter = jitter;
        UseStandardPipeline = useStandardPipeline;
        ProviderMember = providerMember;
    }

    public SystemOneProvider Provider { get; }

    /// <summary>Gets the API key, or <see langword="null"/> when the preset allows none and none is set.</summary>
    public string? ApiKey { get; }

    /// <summary>Gets the <c>Authorization</c> header value, <c>Bearer</c> and the key, or <see langword="null"/> to send none.</summary>
    /// <remarks>Built once here, so no call allocates it.</remarks>
    public string? Authorization { get; }

    /// <summary>Gets the API root, ending in '/'.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets the request path relative to <see cref="BaseAddress"/>.</summary>
    public string Path { get; }

    /// <summary>Gets the resolved default model: the option, trimmed, or the preset's default.</summary>
    public string Model { get; }

    /// <summary>Gets the preset's capabilities with the options' overrides applied.</summary>
    public CapabilitySet Capabilities { get; }

    /// <summary>Gets the provider's telemetry name, such as <c>typesafe</c>.</summary>
    public string ProviderName { get; }

    public TimeSpan Timeout { get; }

    public int MaxRetries { get; }

    public TimeSpan InitialBackoff { get; }

    public TimeSpan MaxRetryDelay { get; }

    public bool Jitter { get; }

    public bool UseStandardPipeline { get; }

    /// <summary>
    /// Gets the <c>,"provider":{…}</c> member every request body ends with, written once from
    /// <see cref="SystemOneDecisionClientOptions.ProviderPreferences"/>; empty when no preference is on.
    /// </summary>
    public ReadOnlyMemory<byte> ProviderMember { get; }

    /// <summary>Gets the <c>provider</c> object alone, <see cref="ProviderMember"/> without its <c>,"provider":</c> name; empty when no preference is on.</summary>
    public ReadOnlyMemory<byte> ProviderObject => ProviderMember.IsEmpty ? ProviderMember : ProviderMember[ProviderMemberPrefixLength..];

    /// <summary>Resolves options: explicit values first, then environment variables, then provider defaults.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public static SystemOneDecisionClientSettings Resolve(SystemOneDecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new SystemOneDecisionClientOptions();

        EnsureKnownProvider(options);
        var providerMember = ResolveProviderMember(options);
        ClientSettingRules.CheckTimeout(options.Timeout, nameof(options));

        RetrySettings.Check(options.MaxRetries, options.InitialBackoff, options.MaxRetryDelay, nameof(options));

        var provider = options.Provider;
        var path = ResolvePath(options);
        var model = ResolveModel(options);
        var capabilities = ResolveCapabilities(options);
        var apiKey = ResolveApiKey(options, environment);
        var baseAddress = ClientSettingRules.WithTrailingSlash(ResolveRoot(options, environment));

        return new SystemOneDecisionClientSettings(
            provider,
            apiKey,
            baseAddress,
            path,
            model,
            capabilities,
            options.Timeout,
            options.MaxRetries,
            options.InitialBackoff,
            options.MaxRetryDelay,
            options.Jitter,
            options.UseStandardPipeline,
            providerMember);
    }

    /// <summary>
    /// Resolves only what an <see cref="HttpClient"/> needs: the base address and the per-attempt time-out. Runs the
    /// same checks as <see cref="Resolve"/> on those, with the same messages, and needs no API key.
    /// </summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">The provider, the time-out, or the base address is invalid.</exception>
    /// <exception cref="InvalidOperationException">The base address environment variable is invalid.</exception>
    public static HttpSettings ResolveHttp(SystemOneDecisionClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new SystemOneDecisionClientOptions();

        EnsureKnownProvider(options);
        ClientSettingRules.CheckTimeout(options.Timeout, nameof(options));

        return new HttpSettings(ClientSettingRules.WithTrailingSlash(ResolveRoot(options, environment)), options.Timeout);
    }

    public override string ToString()
        => "Provider=" + Provider.ToString() + ", BaseAddress=" + BaseAddress.AbsoluteUri + ", ApiKey=***";

    // The length of ,"provider": in front of the object in each member below.
    private const int ProviderMemberPrefixLength = 12;

    // OpenRouter's provider object, as fixed bytes; the members follow OpenRouter's documented names.
    private static readonly byte[] ZeroDataRetentionMember = ",\"provider\":{\"zdr\":true}"u8.ToArray();
    private static readonly byte[] DenyDataCollectionMember = ",\"provider\":{\"data_collection\":\"deny\"}"u8.ToArray();
    private static readonly byte[] BothMembers = ",\"provider\":{\"zdr\":true,\"data_collection\":\"deny\"}"u8.ToArray();

    // A preference is a guarantee the caller relies on, so one that a provider would silently drop refuses to resolve.
    private static ReadOnlyMemory<byte> ResolveProviderMember(SystemOneDecisionClientOptions options)
    {
        var preferences = options.ProviderPreferences;
        if (preferences is null || (!preferences.ZeroDataRetention && !preferences.DenyDataCollection))
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        if (options.Provider != SystemOneProvider.OpenRouter)
        {
            throw new ArgumentException(
                "ProviderPreferences are OpenRouter's provider routing settings; Provider is " + options.Provider.ToString()
                    + ", which does not support them.",
                nameof(options));
        }

        return (preferences.ZeroDataRetention, preferences.DenyDataCollection) switch
        {
            (true, true) => BothMembers,
            (true, false) => ZeroDataRetentionMember,
            _ => DenyDataCollectionMember,
        };
    }

    private static void EnsureKnownProvider(SystemOneDecisionClientOptions options)
    {
        if (!Enum.IsDefined(options.Provider))
        {
            throw new ArgumentException("Unknown provider " + options.Provider.ToString() + ".", nameof(options));
        }
    }

    // Path applies to the presets whose server you name; on a hosted one it would send the key elsewhere on that host.
    // Like ApiKey, a blank Path counts as unset, and surrounding whitespace is trimmed.
    private static string ResolvePath(SystemOneDecisionClientOptions options)
    {
        if (ClientSettingRules.NonBlank(options.Path) is not { } untrimmed)
        {
            return DefaultPath;
        }

        var path = untrimmed.Trim();

        if (options.Provider is not (SystemOneProvider.Custom or SystemOneProvider.VllmSemanticRouter))
        {
            throw new ArgumentException("Path applies only to the Custom and VllmSemanticRouter providers.", nameof(options));
        }

        if (!IsRelativePath(path))
        {
            throw new ArgumentException(
                "The path must be relative, must not start with '/', and must not contain a query, a fragment or '..'.", nameof(options));
        }

        return path;
    }

    private static string ResolveModel(SystemOneDecisionClientOptions options)
    {
        if (options.Model is { } explicitModel)
        {
            return ClientSettingRules.CheckModel(explicitModel, nameof(options));
        }

        if (options.Provider is SystemOneProvider.TypeSafe or SystemOneProvider.OpenRouter)
        {
            return SystemOneDefaults.Model;
        }

        throw new ArgumentException(
            NameOf(options.Provider) + " has no default model. Set SystemOneDecisionClientOptions.Model.", nameof(options));
    }

    // The preset's defaults with each override that is set; a null Capabilities counts as no overrides.
    private static CapabilitySet ResolveCapabilities(SystemOneDecisionClientOptions options)
        => ClientSettingRules.ResolveCapabilities(SystemOneCapabilityDefaults.For(options.Provider), options.Capabilities, nameof(options));

    // The hosted presets require a key, from the option or their own variable. Custom and VllmSemanticRouter send one only
    // when the option sets it, so a vendor key from the environment never reaches a server the caller names.
    private static string? ResolveApiKey(SystemOneDecisionClientOptions options, Func<string, string?> environment)
    {
        if (ClientSettingRules.NonBlank(options.ApiKey) is { } explicitKey)
        {
            return ClientSettingRules.CheckApiKey(explicitKey, nameof(options));
        }

        if (KeyVariable(options.Provider) is not { } keyVariable)
        {
            return null;
        }

        if (ClientSettingRules.NonBlank(environment(keyVariable)) is { } environmentKey)
        {
            return ClientSettingRules.CheckEnvironmentApiKey(environmentKey, keyVariable);
        }

        throw new InvalidOperationException(
            "No API key is configured. Set SystemOneDecisionClientOptions.ApiKey or the " + keyVariable + " environment variable.");
    }

    private static string? KeyVariable(SystemOneProvider provider) => provider switch
    {
        SystemOneProvider.TypeSafe => SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable,
        SystemOneProvider.OpenRouter => SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable,
        _ => null,
    };

    private static Uri ResolveRoot(SystemOneDecisionClientOptions options, Func<string, string?> environment)
    {
        if (options.BaseAddress is { } explicitAddress)
        {
            return ClientSettingRules.CheckBaseAddress(explicitAddress, nameof(options));
        }

        switch (options.Provider)
        {
            case SystemOneProvider.TypeSafe:
                // TYPESAFE_BASE_URL applies only to SystemOneProvider.TypeSafe: no other provider's base address is taken
                // from it, so another provider's key is never sent to a TypeSafe proxy.
                return ClientSettingRules.BaseAddressFromEnvironment(
                        environment(SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable),
                        SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable)
                    ?? SystemOneDefaults.TypeSafeBaseAddress;
            case SystemOneProvider.OpenRouter:
                return SystemOneDefaults.OpenRouterBaseAddress;
            default:
                throw new ArgumentException(NameOf(options.Provider) + " needs a BaseAddress.", nameof(options));
        }
    }

    private static string NameOf(SystemOneProvider provider) => provider switch
    {
        SystemOneProvider.TypeSafe => nameof(SystemOneProvider.TypeSafe),
        SystemOneProvider.OpenRouter => nameof(SystemOneProvider.OpenRouter),
        SystemOneProvider.VllmSemanticRouter => nameof(SystemOneProvider.VllmSemanticRouter),
        _ => nameof(SystemOneProvider.Custom),
    };

    // Relative to the base address and below it: no leading '/', which would drop the base address's own path, no query
    // or fragment, and no '..' segment. A backslash is checked like '/' as defence in depth: {**path} percent-encodes it
    // today, but an http URI would treat an unescaped one as a separator, so a future escaping change cannot open the path.
    private static bool IsRelativePath(string path)
    {
        if (path[0] is '/' or '\\' || path.Contains('?', StringComparison.Ordinal) || path.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        if (!Uri.TryCreate(path, UriKind.Relative, out _))
        {
            return false;
        }

        foreach (var segment in path.Split('/', '\\'))
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
