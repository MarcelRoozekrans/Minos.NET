using Minos.SystemOne;

namespace Minos.Live.Tests;

/// <summary>
/// A fact that runs only when <c>MINOS_LIVE=1</c> is set and the provider's API key is set; the calls are real and
/// billed.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    private readonly string? _skip;
    private string? _requiresEnvironment;

    public LiveFactAttribute(SystemOneProvider provider)
    {
        Provider = provider;
        var variable = provider == SystemOneProvider.OpenRouter
            ? SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable
            : SystemOneDefaults.ApiKeyEnvironmentVariable;
        var optedIn = string.Equals(Environment.GetEnvironmentVariable("MINOS_LIVE"), "1", StringComparison.Ordinal);
        var keySet = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));

        _skip = (optedIn, keySet) switch
        {
            (false, false) => "Set MINOS_LIVE=1 and " + variable + " to run this live test; it calls the real API and is billed.",
            (false, true) => "Set MINOS_LIVE=1 to run this live test; it calls the real API and is billed.",
            (true, false) => "Set " + variable + " to run this live test; it calls the real API and is billed.",
            (true, true) => null,
        };
        Skip = _skip;
    }

    public SystemOneProvider Provider { get; }

    /// <summary>
    /// Gets or sets an extra opt-in variable that must be <c>1</c> for the test to run, such as <c>MINOS_LIVE_LUNA</c>
    /// for a model that needs an OpenRouter account allowing endpoints which retain data: an account that allows only
    /// zero-data-retention endpoints gets a 404 with <c>zdr-violation-by-account</c>.
    /// </summary>
    public string? RequiresEnvironment
    {
        get => _requiresEnvironment;
        set
        {
            _requiresEnvironment = value;
            Skip = _skip is null && value is not null
                && !string.Equals(Environment.GetEnvironmentVariable(value), "1", StringComparison.Ordinal)
                ? "Set " + value + "=1 to run this live test; the model needs an OpenRouter account that allows endpoints which retain data, as zero-data-retention-only accounts get 404 zdr-violation-by-account, and the call is billed."
                : _skip;
        }
    }
}

/// <summary>
/// A fact that runs only when <c>MINOS_LOCAL_SYSTEMONE_URL</c> names a <c>/v1/systemone</c> server to call, such as one
/// the <c>local-systemone</c> workflow starts. The server is local, so nothing is billed and no key is needed.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LocalSystemOneFactAttribute : FactAttribute
{
    /// <summary>The variable holding the server's base address, such as <c>http://127.0.0.1:8000/</c>.</summary>
    public const string UrlVariable = "MINOS_LOCAL_SYSTEMONE_URL";

    /// <summary>The variable holding the model the server serves.</summary>
    public const string ModelVariable = "MINOS_LOCAL_SYSTEMONE_MODEL";

    public LocalSystemOneFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UrlVariable)))
        {
            Skip = "Set " + UrlVariable + " and " + ModelVariable + " to run this test against a local /v1/systemone server.";
        }
    }
}
