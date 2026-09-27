namespace Jev.Net.Transport;

/// <summary>
/// <see cref="JevClientOptions"/> resolved against the environment. A class rather than a record so the API key never
/// appears in a generated <c>ToString</c>.
/// </summary>
internal sealed class JevClientSettings
{
    private JevClientSettings(JevProvider provider, string apiKey, Uri baseAddress, TimeSpan timeout)
    {
        Provider = provider;
        ApiKey = apiKey;
        BaseAddress = baseAddress;
        Timeout = timeout;
    }

    public JevProvider Provider { get; }

    public string ApiKey { get; }

    public Uri BaseAddress { get; }

    public TimeSpan Timeout { get; }

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

        var openRouter = options.Provider == JevProvider.OpenRouter;
        var keyVariable = openRouter ? JevDefaults.OpenRouterApiKeyEnvironmentVariable : JevDefaults.ApiKeyEnvironmentVariable;
        var apiKey = NonBlank(options.ApiKey)
            ?? NonBlank(environment(keyVariable))
            ?? throw new InvalidOperationException(
                "No API key is configured. Set JevClientOptions.ApiKey or the " + keyVariable + " environment variable.");

        var baseAddress = options.BaseAddress
            ?? FromEnvironment(environment(JevDefaults.BaseAddressEnvironmentVariable))
            ?? (openRouter ? JevDefaults.OpenRouterBaseAddress : JevDefaults.TypeSafeBaseAddress);

        if (!baseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("The base address must be an absolute URI.", nameof(options));
        }

        if (options.BaseAddress is not null && HasQueryOrFragment(baseAddress))
        {
            throw new ArgumentException("The base address must not contain a query or fragment.", nameof(options));
        }

        return new JevClientSettings(options.Provider, apiKey, WithTrailingSlash(baseAddress), options.Timeout);
    }

    public override string ToString()
        => "Provider=" + Provider.ToString() + ", BaseAddress=" + BaseAddress.AbsoluteUri + ", ApiKey=***";

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

        return uri;
    }

    private static bool HasQueryOrFragment(Uri uri) => uri.Query.Length > 0 || uri.Fragment.Length > 0;

    private static Uri WithTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
}
