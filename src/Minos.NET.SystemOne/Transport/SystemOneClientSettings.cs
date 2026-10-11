namespace Minos.SystemOne.Transport;

/// <summary>
/// <see cref="SystemOneClientOptions"/> resolved against the environment. A class rather than a record so the API key never
/// appears in a generated <c>ToString</c>.
/// </summary>
internal sealed class SystemOneClientSettings
{
    /// <summary>The <c>/v1/systemone</c> path every preset but Cloudflare posts to, unless <see cref="SystemOneClientOptions.Path"/> overrides it.</summary>
    private const string DefaultPath = "v1/systemone";

    /// <summary>What a Cloudflare model that is not a single path segment is rejected with, by the options and by the transport.</summary>
    public const string CloudflareModelMessage = "A Cloudflare model must be a single name such as clef-flash.";

    private SystemOneClientSettings(
        SystemOneProvider provider,
        string? apiKey,
        Uri baseAddress,
        string? fixedPath,
        string model,
        CapabilitySet capabilities,
        TimeSpan timeout,
        int maxRetries,
        TimeSpan initialBackoff,
        TimeSpan maxRetryDelay,
        bool jitter,
        bool useStandardPipeline)
    {
        Provider = provider;
        ApiKey = apiKey;
        Authorization = apiKey is null ? null : "Bearer " + apiKey;
        BaseAddress = baseAddress;
        FixedPath = fixedPath;
        Model = model;
        Capabilities = capabilities;
        ProviderName = SystemOneTelemetry.ProviderOf(provider);
        Timeout = timeout;
        MaxRetries = maxRetries;
        InitialBackoff = initialBackoff;
        MaxRetryDelay = maxRetryDelay;
        Jitter = jitter;
        UseStandardPipeline = useStandardPipeline;
    }

    public SystemOneProvider Provider { get; }

    /// <summary>Gets the API key, or <see langword="null"/> when the preset allows none and none is set.</summary>
    public string? ApiKey { get; }

    /// <summary>Gets the <c>Authorization</c> header value, <c>Bearer</c> and the key, or <see langword="null"/> to send none.</summary>
    /// <remarks>Built once here, so no call allocates it.</remarks>
    public string? Authorization { get; }

    /// <summary>Gets the API root, ending in '/'; for Cloudflare it includes the account segment.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Gets the request path relative to <see cref="BaseAddress"/>, or <see langword="null"/> for Cloudflare, where the path is the model.</summary>
    public string? FixedPath { get; }

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

    /// <summary>Resolves options: explicit values first, then environment variables, then provider defaults.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public static SystemOneClientSettings Resolve(SystemOneClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new SystemOneClientOptions();

        EnsureKnownProvider(options);
        EnsureValidTimeout(options);

        RetrySettings.Check(options.MaxRetries, options.InitialBackoff, options.MaxRetryDelay, nameof(options));

        var provider = options.Provider;
        var cloudflare = provider == SystemOneProvider.CloudflareWorkersAi;
        if (!cloudflare && NonBlank(options.AccountId) is not null)
        {
            throw new ArgumentException("AccountId applies only to the CloudflareWorkersAi provider.", nameof(options));
        }

        var path = ResolvePath(options);
        var model = ResolveModel(options);
        var capabilities = ResolveCapabilities(options);
        var apiKey = ResolveApiKey(options, environment);
        var baseAddress = ResolveBaseAddress(options, environment);

        return new SystemOneClientSettings(
            provider,
            apiKey,
            baseAddress,
            cloudflare ? null : path,
            model,
            capabilities,
            options.Timeout,
            options.MaxRetries,
            options.InitialBackoff,
            options.MaxRetryDelay,
            options.Jitter,
            options.UseStandardPipeline);
    }

    /// <summary>
    /// Resolves only what an <see cref="HttpClient"/> needs: the base address and the per-attempt time-out. Runs the
    /// same checks as <see cref="Resolve"/> on those, with the same messages, and needs no API key.
    /// </summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">The provider, the time-out, the base address or the Cloudflare account id is invalid.</exception>
    /// <exception cref="InvalidOperationException">The base address environment variable is invalid.</exception>
    public static SystemOneHttpSettings ResolveHttp(SystemOneClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new SystemOneClientOptions();

        EnsureKnownProvider(options);
        EnsureValidTimeout(options);

        return new SystemOneHttpSettings(ResolveBaseAddress(options, environment), options.Timeout);
    }

    /// <summary>Gets the request path for <paramref name="model"/>: <see cref="FixedPath"/>, or the model itself for Cloudflare.</summary>
    public string PathFor(string model) => FixedPath ?? model;

    /// <summary>
    /// Gets whether a request may be sent with <paramref name="model"/>: always, unless the model is the path, as on
    /// Cloudflare, where it must be a single name.
    /// </summary>
    public bool AcceptsModel(string? model) => FixedPath is not null || IsSingleName(model);

    public override string ToString()
        => "Provider=" + Provider.ToString() + ", BaseAddress=" + BaseAddress.AbsoluteUri + ", ApiKey=***";

    private static void EnsureKnownProvider(SystemOneClientOptions options)
    {
        if (!Enum.IsDefined(options.Provider))
        {
            throw new ArgumentException("Unknown provider " + options.Provider.ToString() + ".", nameof(options));
        }
    }

    private static void EnsureValidTimeout(SystemOneClientOptions options)
    {
        if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("The time-out must be positive.", nameof(options));
        }

        if (options.Timeout > RetrySettings.MaxMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Timeout, "The time-out must not exceed " + RetrySettings.MaxMilliseconds.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " milliseconds.");
        }
    }

    // Path applies to the presets whose server you name; on a hosted one it would send the key elsewhere on that host.
    // Like ApiKey and AccountId, a blank Path counts as unset, and surrounding whitespace is trimmed.
    private static string ResolvePath(SystemOneClientOptions options)
    {
        if (NonBlank(options.Path) is not { } untrimmed)
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

    private static string ResolveModel(SystemOneClientOptions options)
    {
        string model;
        if (options.Model is { } explicitModel)
        {
            if (string.IsNullOrWhiteSpace(explicitModel))
            {
                throw new ArgumentException("The model must not be blank.", nameof(options));
            }

            model = explicitModel.Trim();
        }
        else if (options.Provider is SystemOneProvider.TypeSafe or SystemOneProvider.OpenRouter)
        {
            model = SystemOneDefaults.Model;
        }
        else
        {
            throw new ArgumentException(
                NameOf(options.Provider) + " has no default model. Set SystemOneClientOptions.Model.", nameof(options));
        }

        if (options.Provider == SystemOneProvider.CloudflareWorkersAi && !IsSingleName(model))
        {
            throw new ArgumentException(CloudflareModelMessage, nameof(options));
        }

        return model;
    }

    // The preset's defaults with each override that is set; a null Capabilities counts as no overrides.
    private static CapabilitySet ResolveCapabilities(SystemOneClientOptions options)
    {
        var defaults = CapabilitySet.For(options.Provider);
        if (options.Capabilities is not { } overrides)
        {
            return defaults;
        }

        if (overrides.QuestionKinds is { } kinds)
        {
            if (kinds == SystemOneQuestionKinds.None)
            {
                throw new ArgumentException("Capabilities.QuestionKinds must allow at least one question kind.", nameof(options));
            }

            if ((kinds & ~SystemOneQuestionKinds.All) != SystemOneQuestionKinds.None)
            {
                throw new ArgumentException("Capabilities.QuestionKinds has an unknown flag.", nameof(options));
            }
        }

        if (overrides.MaxQuestions is < 1)
        {
            throw new ArgumentException("Capabilities.MaxQuestions must be at least 1.", nameof(options));
        }

        if (overrides.MaxChoiceOptions is < 2)
        {
            throw new ArgumentException("Capabilities.MaxChoiceOptions must be at least 2.", nameof(options));
        }

        return defaults.With(overrides);
    }

    // The hosted presets require a key, from the option or their own variable. Custom and VllmSemanticRouter send one only
    // when the option sets it, so a vendor key from the environment never reaches a server the caller names.
    private static string? ResolveApiKey(SystemOneClientOptions options, Func<string, string?> environment)
    {
        if (NonBlank(options.ApiKey) is { } explicitKey)
        {
            var trimmed = explicitKey.Trim();
            if (HasControlCharacter(trimmed))
            {
                throw new ArgumentException("The API key contains a control character.", nameof(options));
            }

            return trimmed;
        }

        if (KeyVariable(options.Provider) is not { } keyVariable)
        {
            return null;
        }

        if (NonBlank(environment(keyVariable)) is { } environmentKey)
        {
            var trimmed = environmentKey.Trim();
            if (HasControlCharacter(trimmed))
            {
                throw new InvalidOperationException("The " + keyVariable + " environment variable contains a control character.");
            }

            return trimmed;
        }

        throw new InvalidOperationException(
            "No API key is configured. Set SystemOneClientOptions.ApiKey or the " + keyVariable + " environment variable.");
    }

    private static string? KeyVariable(SystemOneProvider provider) => provider switch
    {
        SystemOneProvider.TypeSafe => SystemOneDefaults.ApiKeyEnvironmentVariable,
        SystemOneProvider.OpenRouter => SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable,
        SystemOneProvider.CloudflareWorkersAi => SystemOneDefaults.CloudflareApiTokenEnvironmentVariable,
        _ => null,
    };

    // The address the client sends to, ending in '/': the option or the preset's default, then, for Cloudflare, the
    // account segment.
    private static Uri ResolveBaseAddress(SystemOneClientOptions options, Func<string, string?> environment)
    {
        var root = WithTrailingSlash(ResolveRoot(options, environment));
        if (options.Provider != SystemOneProvider.CloudflareWorkersAi)
        {
            return root;
        }

        // The account id is letters and digits only, so it cannot change the path; the '@' is a literal path character
        // that Uri leaves unescaped.
        return new Uri(root.AbsoluteUri + "accounts/" + ResolveAccountId(options, environment) + "/ai/run/@cf/cloudflare/");
    }

    private static Uri ResolveRoot(SystemOneClientOptions options, Func<string, string?> environment)
    {
        if (options.BaseAddress is { } explicitAddress)
        {
            if (!explicitAddress.IsAbsoluteUri)
            {
                throw new ArgumentException("The base address must be an absolute URI.", nameof(options));
            }

            if (HasQueryOrFragment(explicitAddress))
            {
                throw new ArgumentException("The base address must not contain a query or fragment.", nameof(options));
            }

            if (!IsHttpScheme(explicitAddress))
            {
                throw new ArgumentException("The base address must use the http or https scheme.", nameof(options));
            }

            return explicitAddress;
        }

        switch (options.Provider)
        {
            case SystemOneProvider.TypeSafe:
                // TYPESAFE_BASE_URL applies only to SystemOneProvider.TypeSafe: no other provider's base address is taken
                // from it, so another provider's key is never sent to a TypeSafe proxy.
                return FromEnvironment(environment(SystemOneDefaults.BaseAddressEnvironmentVariable)) ?? SystemOneDefaults.TypeSafeBaseAddress;
            case SystemOneProvider.OpenRouter:
                return SystemOneDefaults.OpenRouterBaseAddress;
            case SystemOneProvider.CloudflareWorkersAi:
                return SystemOneDefaults.CloudflareBaseAddress;
            default:
                throw new ArgumentException(NameOf(options.Provider) + " needs a BaseAddress.", nameof(options));
        }
    }

    private static string ResolveAccountId(SystemOneClientOptions options, Func<string, string?> environment)
    {
        var accountId = NonBlank(options.AccountId) ?? NonBlank(environment(SystemOneDefaults.CloudflareAccountIdEnvironmentVariable));
        if (accountId is null)
        {
            throw new ArgumentException(
                "CloudflareWorkersAi needs an AccountId. Set SystemOneClientOptions.AccountId or the "
                    + SystemOneDefaults.CloudflareAccountIdEnvironmentVariable + " environment variable.",
                nameof(options));
        }

        accountId = accountId.Trim();
        foreach (var c in accountId)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                throw new ArgumentException("The Cloudflare account id must contain only letters and digits.", nameof(options));
            }
        }

        return accountId;
    }

    private static string NameOf(SystemOneProvider provider) => provider switch
    {
        SystemOneProvider.TypeSafe => nameof(SystemOneProvider.TypeSafe),
        SystemOneProvider.OpenRouter => nameof(SystemOneProvider.OpenRouter),
        SystemOneProvider.CloudflareWorkersAi => nameof(SystemOneProvider.CloudflareWorkersAi),
        SystemOneProvider.VllmSemanticRouter => nameof(SystemOneProvider.VllmSemanticRouter),
        _ => nameof(SystemOneProvider.Custom),
    };

    // A Cloudflare model is the last path segment: letters, digits, '.', '_' and '-' only, and never '.' or '..', which
    // would move the request up the account's path.
    private static bool IsSingleName(string? model)
    {
        if (string.IsNullOrEmpty(model) || model is "." or "..")
        {
            return false;
        }

        foreach (var c in model)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

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

    private static bool HasControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Uri? FromEnvironment(string? value)
    {
        if (NonBlank(value) is not { } text)
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "The " + SystemOneDefaults.BaseAddressEnvironmentVariable + " environment variable is not an absolute URI.");
        }

        if (HasQueryOrFragment(uri))
        {
            throw new InvalidOperationException(
                "The " + SystemOneDefaults.BaseAddressEnvironmentVariable + " environment variable must not contain a query or fragment.");
        }

        if (!IsHttpScheme(uri))
        {
            throw new InvalidOperationException(
                "The " + SystemOneDefaults.BaseAddressEnvironmentVariable + " environment variable must use the http or https scheme.");
        }

        return uri;
    }

    private static bool HasQueryOrFragment(Uri uri) => uri.Query.Length > 0 || uri.Fragment.Length > 0;

    private static bool IsHttpScheme(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

    private static Uri WithTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
