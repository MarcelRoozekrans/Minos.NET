namespace ZeroAlloc.Jev.Tests;

/// <summary>
/// <see cref="JevClient.ConfigureHttpClient(HttpClient, JevClientOptions)"/>: what an <see cref="HttpClient"/> from
/// <c>IHttpClientFactory</c> gets, through the internal overload with a fake environment.
/// </summary>
public sealed class ConfigureHttpClientTests
{
    [Fact]
    public void BaseAddressOption_IsSet_WithATrailingSlash()
    {
        using var http = new HttpClient();

        Configure(http, new JevClientOptions { BaseAddress = new Uri("http://proxy.local/jev") });

        Assert.Equal(new Uri("http://proxy.local/jev/"), http.BaseAddress);
    }

    [Fact]
    public void ExistingBaseAddress_IsKept()
    {
        using var http = new HttpClient { BaseAddress = new Uri("http://mine.local/api/") };

        Configure(http, new JevClientOptions { BaseAddress = new Uri("http://option.local/") });

        Assert.Equal(new Uri("http://mine.local/api/"), http.BaseAddress);
    }

    [Fact]
    public void TypeSafeBaseAddressEnvironment_IsSet()
    {
        using var http = new HttpClient();

        Configure(http, new JevClientOptions(), ("TYPESAFE_BASE_URL", "http://env.local/jev"));

        Assert.Equal(new Uri("http://env.local/jev/"), http.BaseAddress);
    }

    [Fact]
    public void OpenRouter_GetsItsDefaultAddress_AndIgnoresTheTypeSafeEnvironment()
    {
        using var http = new HttpClient();

        Configure(http, new JevClientOptions { Provider = JevProvider.OpenRouter }, ("TYPESAFE_BASE_URL", "http://env.local/"));

        Assert.Equal(new Uri("https://openrouter.ai/api/"), http.BaseAddress);
    }

    [Fact]
    public void NullOptions_UseTheDefaults()
    {
        using var http = new HttpClient();

        Configure(http, null);

        Assert.Equal(new Uri("https://api.typesafe.ai/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(60), http.Timeout);
    }

    [Fact]
    public void Timeout_IsThePerAttemptTimeout()
    {
        using var http = new HttpClient();

        Configure(http, new JevClientOptions { Timeout = TimeSpan.FromSeconds(7) });

        Assert.Equal(TimeSpan.FromSeconds(7), http.Timeout);
    }

    [Fact]
    public void InfiniteTimeout_IsSet()
    {
        using var http = new HttpClient();

        Configure(http, new JevClientOptions { Timeout = Timeout.InfiniteTimeSpan });

        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    [Fact]
    public void UserAgent_IsAddedOnce()
    {
        using var http = new HttpClient();

        Configure(http, null);
        Configure(http, null);

        // One product token, ZeroAlloc.Jev/{version}, with no build metadata.
        Assert.Matches("^ZeroAlloc\\.Jev/[^ +]+$", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void NoApiKey_IsNeeded()
    {
        using var http = new HttpClient();

        var exception = Record.Exception(() => Configure(http, new JevClientOptions { ApiKey = null }));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTimeout_Throws_WithTheSettingsMessage(int seconds)
    {
        using var http = new HttpClient();

        var exception = Assert.Throws<ArgumentException>(
            () => Configure(http, new JevClientOptions { Timeout = TimeSpan.FromSeconds(seconds) }));

        Assert.StartsWith("The time-out must be positive.", exception.Message, StringComparison.Ordinal);
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void RelativeBaseAddressOption_Throws_WithTheSettingsMessage()
    {
        using var http = new HttpClient();

        var exception = Assert.Throws<ArgumentException>(
            () => Configure(http, new JevClientOptions { BaseAddress = new Uri("v1", UriKind.Relative) }));

        Assert.StartsWith("The base address must be an absolute URI.", exception.Message, StringComparison.Ordinal);
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void BaseAddressOptionWithQuery_Throws_WithTheSettingsMessage()
    {
        using var http = new HttpClient();

        var exception = Assert.Throws<ArgumentException>(
            () => Configure(http, new JevClientOptions { BaseAddress = new Uri("http://host/api?key=value") }));

        Assert.StartsWith("The base address must not contain a query or fragment.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidBaseAddressEnvironment_Throws_NamingTheVariable()
    {
        using var http = new HttpClient();

        var exception = Assert.Throws<InvalidOperationException>(
            () => Configure(http, new JevClientOptions(), ("TYPESAFE_BASE_URL", "not a url")));

        Assert.Equal("The TYPESAFE_BASE_URL environment variable is not an absolute URI.", exception.Message);
    }

    [Fact]
    public void UnknownProvider_Throws_WithTheSettingsMessage()
    {
        using var http = new HttpClient();

        var exception = Assert.Throws<ArgumentException>(
            () => Configure(http, new JevClientOptions { Provider = (JevProvider)42 }));

        Assert.StartsWith("Unknown provider 42.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooLongTimeout_Throws_AndLeavesTheClientUnchanged()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Configure(http, new JevClientOptions { Timeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1), BaseAddress = new Uri("http://option.local/") }));

        Assert.StartsWith("The time-out must not exceed", exception.Message, StringComparison.Ordinal);
        Assert.Equal("options", exception.ParamName);
        Assert.Null(http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(100), http.Timeout);
        Assert.Empty(http.DefaultRequestHeaders.UserAgent);
    }

    [Fact]
    public void MaximumTimeout_IsAccepted()
    {
        using var http = new HttpClient();

        Configure(http, new JevClientOptions { Timeout = TimeSpan.FromMilliseconds(int.MaxValue) });

        Assert.Equal(TimeSpan.FromMilliseconds(int.MaxValue), http.Timeout);
    }

    [Fact]
    public void TooLongTimeout_Throws_FromTheConstructor()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new JevClient(new JevClientOptions { ApiKey = "k", Timeout = TimeSpan.FromMilliseconds((double)int.MaxValue + 1) }));

    [Fact]
    public void FailedConfiguration_LeavesTheClientUnchanged()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(100) };

        _ = Assert.Throws<ArgumentException>(
            () => Configure(http, new JevClientOptions { Timeout = TimeSpan.Zero, BaseAddress = new Uri("http://option.local/") }));

        Assert.Null(http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(100), http.Timeout);
        Assert.Empty(http.DefaultRequestHeaders.UserAgent);
    }

    [Fact]
    public void NullHttpClient_Throws()
        => Assert.Throws<ArgumentNullException>(() => JevClient.ConfigureHttpClient(null!, new JevClientOptions()));

    [Fact]
    public void PublicOverload_ConfiguresTheClient()
    {
        using var http = new HttpClient();

        JevClient.ConfigureHttpClient(http, new JevClientOptions { BaseAddress = new Uri("http://option.local/"), Timeout = TimeSpan.FromSeconds(5) });

        Assert.Equal(new Uri("http://option.local/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(5), http.Timeout);
        Assert.Matches("^ZeroAlloc\\.Jev/[^ +]+$", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    private static void Configure(HttpClient http, JevClientOptions? options, params (string Name, string Value)[] environment)
        => JevClient.ConfigureHttpClient(
            http,
            options,
            name => Array.Find(environment, variable => string.Equals(variable.Name, name, StringComparison.Ordinal)).Value);
}
