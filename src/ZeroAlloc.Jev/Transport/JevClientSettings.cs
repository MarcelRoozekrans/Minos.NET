namespace ZeroAlloc.Jev.Transport;

/// <summary>
/// <see cref="JevClientOptions"/> resolved against the environment. A class rather than a record so the API key never
/// appears in a generated <c>ToString</c>.
/// </summary>
internal sealed class JevClientSettings
{
    private JevClientSettings(
        JevProvider provider,
        string apiKey,
        Uri baseAddress,
        string model,
        TimeSpan timeout,
        int maxRetries,
        TimeSpan initialBackoff,
        TimeSpan maxRetryDelay,
        bool jitter)
    {
        Provider = provider;
        ApiKey = apiKey;
        BaseAddress = baseAddress;
        Model = model;
        Timeout = timeout;
        MaxRetries = maxRetries;
        InitialBackoff = initialBackoff;
        MaxRetryDelay = maxRetryDelay;
        Jitter = jitter;
    }

    public JevProvider Provider { get; }

    public string ApiKey { get; }

    public Uri BaseAddress { get; }

    public string Model { get; }

    public TimeSpan Timeout { get; }

    public int MaxRetries { get; }

    public TimeSpan InitialBackoff { get; }

    public TimeSpan MaxRetryDelay { get; }

    public bool Jitter { get; }

    /// <summary>Resolves options: explicit values first, then environment variables, then provider defaults.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for all defaults.</param>
    /// <param name="environment">Reads an environment variable; tests pass a fake.</param>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    /// <exception cref="InvalidOperationException">No API key is configured, or an environment variable is invalid.</exception>
    public static JevClientSettings Resolve(JevClientOptions? options, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        options ??= new JevClientOptions();

        if (!Enum.IsDefined(options.Provider))
        {
            throw new ArgumentException("Unknown provider " + options.Provider.ToString() + ".", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero && options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("The time-out must be positive.", nameof(options));
        }

        if (options.MaxRetries is < 0 or > 10)
        {
            throw new ArgumentException("MaxRetries must be between 0 and 10.", nameof(options));
        }

        var maxMilliseconds = TimeSpan.FromMilliseconds(int.MaxValue);
        if (options.InitialBackoff <= TimeSpan.Zero || options.InitialBackoff > maxMilliseconds)
        {
            throw new ArgumentException("InitialBackoff must be positive and at most int.MaxValue milliseconds.", nameof(options));
        }

        if (options.MaxRetryDelay < options.InitialBackoff || options.MaxRetryDelay > maxMilliseconds)
        {
            throw new ArgumentException(
                "MaxRetryDelay must be at least InitialBackoff and at most int.MaxValue milliseconds.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ArgumentException("The model must not be blank.", nameof(options));
        }

        var openRouter = options.Provider == JevProvider.OpenRouter;
        var apiKey = ResolveApiKey(options, openRouter, environment);
        var baseAddress = ResolveBaseAddress(options, openRouter, environment);

        return new JevClientSettings(
            options.Provider,
            apiKey,
            WithTrailingSlash(baseAddress),
            options.Model.Trim(),
            options.Timeout,
            options.MaxRetries,
            options.InitialBackoff,
            options.MaxRetryDelay,
            options.Jitter);
    }

    public override string ToString()
        => "Provider=" + Provider.ToString() + ", BaseAddress=" + BaseAddress.AbsoluteUri + ", ApiKey=***";

    private static string ResolveApiKey(JevClientOptions options, bool openRouter, Func<string, string?> environment)
    {
        var keyVariable = openRouter ? JevDefaults.OpenRouterApiKeyEnvironmentVariable : JevDefaults.ApiKeyEnvironmentVariable;

        if (NonBlank(options.ApiKey) is { } explicitKey)
        {
            var trimmed = explicitKey.Trim();
            if (HasControlCharacter(trimmed))
            {
                throw new ArgumentException("The API key contains a control character.", nameof(options));
            }

            return trimmed;
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
            "No API key is configured. Set JevClientOptions.ApiKey or the " + keyVariable + " environment variable.");
    }

    private static Uri ResolveBaseAddress(JevClientOptions options, bool openRouter, Func<string, string?> environment)
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

        // TYPESAFE_BASE_URL applies only to JevProvider.TypeSafe: OpenRouter's base address is never taken from it, so
        // an OpenRouter key is never sent to a TypeSafe proxy.
        if (!openRouter && FromEnvironment(environment(JevDefaults.BaseAddressEnvironmentVariable)) is { } environmentAddress)
        {
            return environmentAddress;
        }

        return openRouter ? JevDefaults.OpenRouterBaseAddress : JevDefaults.TypeSafeBaseAddress;
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
                "The " + JevDefaults.BaseAddressEnvironmentVariable + " environment variable is not an absolute URI.");
        }

        if (HasQueryOrFragment(uri))
        {
            throw new InvalidOperationException(
                "The " + JevDefaults.BaseAddressEnvironmentVariable + " environment variable must not contain a query or fragment.");
        }

        if (!IsHttpScheme(uri))
        {
            throw new InvalidOperationException(
                "The " + JevDefaults.BaseAddressEnvironmentVariable + " environment variable must use the http or https scheme.");
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
