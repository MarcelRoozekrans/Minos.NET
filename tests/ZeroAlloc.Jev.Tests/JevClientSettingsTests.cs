using ZeroAlloc.Jev.Transport;

namespace ZeroAlloc.Jev.Tests;

public sealed class JevClientSettingsTests
{
    [Fact]
    public void Defaults_TypeSafe()
    {
        var settings = Resolve(null, ("TYPESAFE_API_KEY", "env-key"));

        Assert.Equal(JevProvider.TypeSafe, settings.Provider);
        Assert.Equal("env-key", settings.ApiKey);
        Assert.Equal(new Uri("https://api.typesafe.ai/"), settings.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Timeout);
    }

    [Fact]
    public void OpenRouter_UsesItsKeyVariableAndBaseAddress()
    {
        var settings = Resolve(
            new JevClientOptions { Provider = JevProvider.OpenRouter },
            ("TYPESAFE_API_KEY", "typesafe-key"),
            ("OPENROUTER_API_KEY", "openrouter-key"));

        Assert.Equal("openrouter-key", settings.ApiKey);
        Assert.Equal(new Uri("https://openrouter.ai/api/"), settings.BaseAddress);
    }

    [Fact]
    public void ExplicitApiKey_WinsOverEnvironment()
        => Assert.Equal("explicit", Resolve(new JevClientOptions { ApiKey = "explicit" }, ("TYPESAFE_API_KEY", "env-key")).ApiKey);

    [Fact]
    public void BlankApiKey_FallsBackToEnvironment()
        => Assert.Equal("env-key", Resolve(new JevClientOptions { ApiKey = "  " }, ("TYPESAFE_API_KEY", "env-key")).ApiKey);

    [Theory]
    [InlineData(JevProvider.TypeSafe, "TYPESAFE_API_KEY")]
    [InlineData(JevProvider.OpenRouter, "OPENROUTER_API_KEY")]
    public void MissingApiKey_Throws_NamingTheVariable(JevProvider provider, string variable)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(new JevClientOptions { Provider = provider }));

        Assert.Contains(variable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseAddressOption_WinsOverEnvironment()
    {
        var settings = Resolve(
            new JevClientOptions { ApiKey = "k", BaseAddress = new Uri("http://option.local/") },
            ("TYPESAFE_BASE_URL", "http://env.local/"));

        Assert.Equal(new Uri("http://option.local/"), settings.BaseAddress);
    }

    [Fact]
    public void TypeSafe_BaseAddressEnvironment_WinsOverProviderDefault()
        => Assert.Equal(
            new Uri("http://env.local/"),
            Resolve(new JevClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "http://env.local/")).BaseAddress);

    [Fact]
    public void OpenRouter_IgnoresTypeSafeBaseAddressEnvironment()
        => Assert.Equal(
            new Uri("https://openrouter.ai/api/"),
            Resolve(new JevClientOptions { ApiKey = "k", Provider = JevProvider.OpenRouter }, ("TYPESAFE_BASE_URL", "http://env.local/")).BaseAddress);

    [Fact]
    public void BaseAddress_GetsTrailingSlash()
        => Assert.Equal(
            new Uri("http://localhost:5000/api/"),
            Resolve(new JevClientOptions { ApiKey = "k", BaseAddress = new Uri("http://localhost:5000/api") }).BaseAddress);

    [Fact]
    public void InvalidBaseAddressEnvironment_Throws_NamingTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(new JevClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "not a url")));

        Assert.Contains("TYPESAFE_BASE_URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeBaseAddressOption_Throws()
        => Assert.Throws<ArgumentException>(
            () => Resolve(new JevClientOptions { ApiKey = "k", BaseAddress = new Uri("v1", UriKind.Relative) }));

    [Fact]
    public void BaseAddressOptionWithQuery_Throws()
        => Assert.Throws<ArgumentException>(
            () => Resolve(new JevClientOptions { ApiKey = "k", BaseAddress = new Uri("http://host/api?key=value") }));

    [Fact]
    public void BaseAddressOptionWithFragment_Throws()
        => Assert.Throws<ArgumentException>(
            () => Resolve(new JevClientOptions { ApiKey = "k", BaseAddress = new Uri("http://host/api#x") }));

    [Fact]
    public void BaseAddressEnvironmentWithQuery_Throws_NamingTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(new JevClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "http://host/api?key=value")));

        Assert.Contains("TYPESAFE_BASE_URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseAddressOptionWithNonHttpScheme_Throws()
        => Assert.Throws<ArgumentException>(
            () => Resolve(new JevClientOptions { ApiKey = "k", BaseAddress = new Uri("ftp://host/api/") }));

    [Fact]
    public void BaseAddressEnvironmentWithNonHttpScheme_Throws_NamingTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(new JevClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "ftp://host/api/")));

        Assert.Contains("TYPESAFE_BASE_URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeyOption_TrimsSurroundingWhitespace()
        => Assert.Equal("abc", Resolve(new JevClientOptions { ApiKey = "abc\n" }).ApiKey);

    [Fact]
    public void ApiKeyEnvironment_TrimsSurroundingWhitespace()
        => Assert.Equal("env-key", Resolve(null, ("TYPESAFE_API_KEY", "env-key\n")).ApiKey);

    [Fact]
    public void ApiKeyOption_WithEmbeddedControlCharacter_Throws_WithoutTheKey()
    {
        var key = "ab" + (char)1 + "cd";

        var exception = Assert.Throws<ArgumentException>(() => Resolve(new JevClientOptions { ApiKey = key }));

        Assert.DoesNotContain(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeyEnvironment_WithEmbeddedControlCharacter_Throws_NamingTheVariable_WithoutTheKey()
    {
        var key = "ab" + (char)1 + "cd";

        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(null, ("TYPESAFE_API_KEY", key)));

        Assert.DoesNotContain(key, exception.Message, StringComparison.Ordinal);
        Assert.Contains("TYPESAFE_API_KEY", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveTimeout_Throws(int seconds)
        => Assert.Throws<ArgumentException>(
            () => Resolve(new JevClientOptions { ApiKey = "k", Timeout = TimeSpan.FromSeconds(seconds) }));

    [Fact]
    public void InfiniteTimeout_IsAllowed()
        => Assert.Equal(
            Timeout.InfiniteTimeSpan,
            Resolve(new JevClientOptions { ApiKey = "k", Timeout = Timeout.InfiniteTimeSpan }).Timeout);

    [Fact]
    public void UnknownProvider_Throws()
        => Assert.Throws<ArgumentException>(() => Resolve(new JevClientOptions { ApiKey = "k", Provider = (JevProvider)42 }));

    [Fact]
    public void ToString_DoesNotRevealTheApiKey()
        => Assert.DoesNotContain("secret-key", Resolve(new JevClientOptions { ApiKey = "secret-key" }).ToString(), StringComparison.Ordinal);

    private static JevClientSettings Resolve(JevClientOptions? options, params (string Name, string Value)[] environment)
        => JevClientSettings.Resolve(
            options,
            name => Array.Find(environment, variable => string.Equals(variable.Name, name, StringComparison.Ordinal)).Value);
}
