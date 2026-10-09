namespace Minos.Tests;

/// <summary>
/// Every invalid <see cref="DecisionClientOptions"/> case: the options, the environment variables it runs against, and the
/// exception the settings resolver throws for it. <see cref="DecisionClientSettingsTests"/> and
/// <see cref="DecisionClientOptionsValidateTests"/> both run every case, so a case added here is covered by both.
/// </summary>
public static class InvalidOptionsCases
{
    private static readonly Dictionary<string, InvalidOptionsCase> Cases = new(StringComparer.Ordinal)
    {
        ["UnknownProvider"] = new(() => new() { ApiKey = "k", Provider = (DecisionProvider)42 }, typeof(ArgumentException)),
        ["ZeroTimeout"] = new(() => new() { ApiKey = "k", Timeout = TimeSpan.Zero }, typeof(ArgumentException)),
        ["NegativeTimeout"] = new(() => new() { ApiKey = "k", Timeout = TimeSpan.FromSeconds(-1) }, typeof(ArgumentException)),
        ["TimeoutBeyondIntMilliseconds"] = new(
            () => new() { ApiKey = "k", Timeout = TimeSpan.FromMilliseconds(int.MaxValue + 1.0) },
            typeof(ArgumentOutOfRangeException)),
        ["NegativeMaxRetries"] = new(() => new() { ApiKey = "k", MaxRetries = -1 }, typeof(ArgumentException)),
        ["ElevenMaxRetries"] = new(() => new() { ApiKey = "k", MaxRetries = 11 }, typeof(ArgumentException)),
        ["ZeroInitialBackoff"] = new(() => new() { ApiKey = "k", InitialBackoff = TimeSpan.Zero }, typeof(ArgumentException)),
        ["NegativeInitialBackoff"] = new(
            () => new() { ApiKey = "k", InitialBackoff = TimeSpan.FromMilliseconds(-1) }, typeof(ArgumentException)),
        ["InitialBackoffBeyondIntMilliseconds"] = new(
            () => new() { ApiKey = "k", InitialBackoff = TimeSpan.FromDays(30), MaxRetryDelay = TimeSpan.FromDays(30) },
            typeof(ArgumentException)),
        ["MaxRetryDelayBelowInitialBackoff"] = new(
            () => new() { ApiKey = "k", InitialBackoff = TimeSpan.FromSeconds(2), MaxRetryDelay = TimeSpan.FromSeconds(1) },
            typeof(ArgumentException)),
        ["MaxRetryDelayBeyondIntMilliseconds"] = new(
            () => new() { ApiKey = "k", MaxRetryDelay = TimeSpan.FromDays(30) }, typeof(ArgumentException)),
        ["EmptyModel"] = new(() => new() { ApiKey = "k", Model = string.Empty }, typeof(ArgumentException)),
        ["WhiteSpaceModel"] = new(() => new() { ApiKey = "k", Model = " \t\n" }, typeof(ArgumentException)),
        ["NullModel"] = new(() => new() { ApiKey = "k", Model = null! }, typeof(ArgumentException)),
        ["ApiKeyWithControlCharacter"] = new(() => new() { ApiKey = "ab\u0001cd" }, typeof(ArgumentException)),
        ["RelativeBaseAddress"] = new(
            () => new() { ApiKey = "k", BaseAddress = new Uri("v1", UriKind.Relative) }, typeof(ArgumentException)),
        ["BaseAddressWithQuery"] = new(
            () => new() { ApiKey = "k", BaseAddress = new Uri("http://host/api?key=value") }, typeof(ArgumentException)),
        ["BaseAddressWithFragment"] = new(
            () => new() { ApiKey = "k", BaseAddress = new Uri("http://host/api#x") }, typeof(ArgumentException)),
        ["BaseAddressWithNonHttpScheme"] = new(
            () => new() { ApiKey = "k", BaseAddress = new Uri("ftp://host/api/") }, typeof(ArgumentException)),
        ["MissingTypeSafeApiKey"] = new(() => new(), typeof(InvalidOperationException)),
        ["MissingOpenRouterApiKey"] = new(() => new() { Provider = DecisionProvider.OpenRouter }, typeof(InvalidOperationException)),
        ["ApiKeyEnvironmentWithControlCharacter"] = new(
            () => new(), typeof(InvalidOperationException), ("TYPESAFE_API_KEY", "ab\u0001cd")),
        ["BaseAddressEnvironmentNotAUri"] = new(
            () => new() { ApiKey = "k" }, typeof(InvalidOperationException), ("TYPESAFE_BASE_URL", "not a url")),
        ["BaseAddressEnvironmentWithQuery"] = new(
            () => new() { ApiKey = "k" }, typeof(InvalidOperationException), ("TYPESAFE_BASE_URL", "http://host/api?key=value")),
        ["BaseAddressEnvironmentWithNonHttpScheme"] = new(
            () => new() { ApiKey = "k" }, typeof(InvalidOperationException), ("TYPESAFE_BASE_URL", "ftp://host/api/")),
    };

    /// <summary>Every case's name.</summary>
    public static TheoryData<string> All => Names(static _ => true);

    /// <summary>
    /// The cases that read no environment variable: they set no fake variable, and their options carry an API key. With
    /// a key set, the resolver throws before it would read <c>TYPESAFE_BASE_URL</c>, or never reads it because the
    /// base address is set too. So these cases throw the same in any process environment.
    /// </summary>
    public static TheoryData<string> OptionsOnly
        => Names(static invalid => invalid.Environment.Length == 0 && !string.IsNullOrWhiteSpace(invalid.Options().ApiKey));

    /// <summary>Gets the case named <paramref name="name"/>.</summary>
    public static InvalidOptionsCase Get(string name) => Cases[name];

    private static TheoryData<string> Names(Func<InvalidOptionsCase, bool> include)
    {
        var names = new TheoryData<string>();
        foreach (var (name, invalid) in Cases)
        {
            if (include(invalid))
            {
                names.Add(name);
            }
        }

        return names;
    }
}

/// <summary>One invalid options case.</summary>
/// <param name="options">Creates the case's options; each call returns a fresh instance.</param>
/// <param name="exception">The exact exception type the resolver throws.</param>
/// <param name="environment">The fake environment variables the case runs against.</param>
public sealed class InvalidOptionsCase(
    Func<DecisionClientOptions> options, Type exception, params (string Name, string Value)[] environment)
{
    /// <summary>Gets the exact exception type the resolver throws.</summary>
    public Type Exception { get; } = exception;

    /// <summary>Gets the fake environment variables the case runs against.</summary>
    public (string Name, string Value)[] Environment { get; } = environment;

    /// <summary>Creates the case's options.</summary>
    public DecisionClientOptions Options() => options();

    /// <summary>Reads a fake environment variable, or <see langword="null"/> when the case does not set it.</summary>
    public string? Lookup(string name)
        => Array.Find(Environment, variable => string.Equals(variable.Name, name, StringComparison.Ordinal)).Value;
}
