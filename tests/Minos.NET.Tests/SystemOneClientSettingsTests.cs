using Minos.SystemOne;
using Minos.SystemOne.Transport;

namespace Minos.Tests;

public sealed class SystemOneClientSettingsTests
{
    [Fact]
    public void Defaults_TypeSafe()
    {
        var settings = Resolve(null, ("TYPESAFE_API_KEY", "env-key"));

        Assert.Equal(SystemOneProvider.TypeSafe, settings.Provider);
        Assert.Equal("env-key", settings.ApiKey);
        Assert.Equal(new Uri("https://api.typesafe.ai/"), settings.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Timeout);
    }

    [Fact]
    public void OpenRouter_UsesItsKeyVariableAndBaseAddress()
    {
        var settings = Resolve(
            new SystemOneClientOptions { Provider = SystemOneProvider.OpenRouter },
            ("TYPESAFE_API_KEY", "typesafe-key"),
            ("OPENROUTER_API_KEY", "openrouter-key"));

        Assert.Equal("openrouter-key", settings.ApiKey);
        Assert.Equal(new Uri("https://openrouter.ai/api/"), settings.BaseAddress);
    }

    [Fact]
    public void ExplicitApiKey_WinsOverEnvironment()
        => Assert.Equal("explicit", Resolve(new SystemOneClientOptions { ApiKey = "explicit" }, ("TYPESAFE_API_KEY", "env-key")).ApiKey);

    [Fact]
    public void BlankApiKey_FallsBackToEnvironment()
        => Assert.Equal("env-key", Resolve(new SystemOneClientOptions { ApiKey = "  " }, ("TYPESAFE_API_KEY", "env-key")).ApiKey);

    [Theory]
    [InlineData(SystemOneProvider.TypeSafe, "TYPESAFE_API_KEY")]
    [InlineData(SystemOneProvider.OpenRouter, "OPENROUTER_API_KEY")]
    public void MissingApiKey_Throws_NamingTheVariable(SystemOneProvider provider, string variable)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(new SystemOneClientOptions { Provider = provider }));

        Assert.Contains(variable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseAddressOption_WinsOverEnvironment()
    {
        var settings = Resolve(
            new SystemOneClientOptions { ApiKey = "k", BaseAddress = new Uri("http://option.local/") },
            ("TYPESAFE_BASE_URL", "http://env.local/"));

        Assert.Equal(new Uri("http://option.local/"), settings.BaseAddress);
    }

    [Fact]
    public void TypeSafe_BaseAddressEnvironment_WinsOverProviderDefault()
        => Assert.Equal(
            new Uri("http://env.local/"),
            Resolve(new SystemOneClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "http://env.local/")).BaseAddress);

    [Fact]
    public void OpenRouter_IgnoresTypeSafeBaseAddressEnvironment()
        => Assert.Equal(
            new Uri("https://openrouter.ai/api/"),
            Resolve(new SystemOneClientOptions { ApiKey = "k", Provider = SystemOneProvider.OpenRouter }, ("TYPESAFE_BASE_URL", "http://env.local/")).BaseAddress);

    [Fact]
    public void BaseAddress_GetsTrailingSlash()
        => Assert.Equal(
            new Uri("http://localhost:5000/api/"),
            Resolve(new SystemOneClientOptions { ApiKey = "k", BaseAddress = new Uri("http://localhost:5000/api") }).BaseAddress);

    [Fact]
    public void InvalidBaseAddressEnvironment_Throws_NamingTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(new SystemOneClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "not a url")));

        Assert.Contains("TYPESAFE_BASE_URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseAddressEnvironmentWithQuery_Throws_NamingTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(new SystemOneClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "http://host/api?key=value")));

        Assert.Contains("TYPESAFE_BASE_URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseAddressEnvironmentWithNonHttpScheme_Throws_NamingTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(new SystemOneClientOptions { ApiKey = "k" }, ("TYPESAFE_BASE_URL", "ftp://host/api/")));

        Assert.Contains("TYPESAFE_BASE_URL", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeyOption_TrimsSurroundingWhitespace()
        => Assert.Equal("abc", Resolve(new SystemOneClientOptions { ApiKey = "abc\n" }).ApiKey);

    [Fact]
    public void ApiKeyEnvironment_TrimsSurroundingWhitespace()
        => Assert.Equal("env-key", Resolve(null, ("TYPESAFE_API_KEY", "env-key\n")).ApiKey);

    [Fact]
    public void ApiKeyOption_WithEmbeddedControlCharacter_Throws_WithoutTheKey()
    {
        var key = "ab" + (char)1 + "cd";

        var exception = Assert.Throws<ArgumentException>(() => Resolve(new SystemOneClientOptions { ApiKey = key }));

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

    [Fact]
    public void InfiniteTimeout_IsAllowed()
        => Assert.Equal(
            Timeout.InfiniteTimeSpan,
            Resolve(new SystemOneClientOptions { ApiKey = "k", Timeout = Timeout.InfiniteTimeSpan }).Timeout);

    [Fact]
    public void ToString_DoesNotRevealTheApiKey()
        => Assert.DoesNotContain("secret-key", Resolve(new SystemOneClientOptions { ApiKey = "secret-key" }).ToString(), StringComparison.Ordinal);

    [Fact]
    public void RetryDefaults_MatchTheOfficialSdk()
    {
        var settings = Resolve(new SystemOneClientOptions { ApiKey = "k" });

        Assert.Equal(2, settings.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), settings.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.MaxRetryDelay);
        Assert.True(settings.Jitter);
    }

    [Fact]
    public void RetryOptions_AreCarriedOver()
    {
        var settings = Resolve(new SystemOneClientOptions
        {
            ApiKey = "k",
            MaxRetries = 0,
            InitialBackoff = TimeSpan.FromMilliseconds(10),
            MaxRetryDelay = TimeSpan.FromSeconds(2),
            Jitter = false,
        });

        Assert.Equal(0, settings.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(10), settings.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(2), settings.MaxRetryDelay);
        Assert.False(settings.Jitter);
    }

    [Theory]
    [InlineData(SystemOneProvider.TypeSafe)]
    [InlineData(SystemOneProvider.OpenRouter)]
    public void NullModel_ResolvesToThePresetDefault(SystemOneProvider provider)
        => Assert.Equal("jev-latest", Resolve(new SystemOneClientOptions { ApiKey = "k", Provider = provider, Model = null }).Model);

    [Theory]
    [InlineData(SystemOneProvider.CloudflareWorkersAi)]
    [InlineData(SystemOneProvider.VllmSemanticRouter)]
    [InlineData(SystemOneProvider.Custom)]
    public void NullModel_WithoutAPresetDefault_Throws_NamingTheProvider(SystemOneProvider provider)
    {
        var options = new SystemOneClientOptions { ApiKey = "k", Provider = provider, BaseAddress = new Uri("http://host/") };
        if (provider == SystemOneProvider.CloudflareWorkersAi)
        {
            options.AccountId = "abc123";
        }

        var exception = Assert.Throws<ArgumentException>(() => Resolve(options));

        Assert.Equal("options", exception.ParamName);
        Assert.StartsWith(provider.ToString() + " has no default model. Set SystemOneClientOptions.Model.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Model_DefaultsToDecisionDefaultsModel()
        => Assert.Equal(SystemOneDefaults.Model, Resolve(new SystemOneClientOptions { ApiKey = "k" }).Model);

    [Fact]
    public void ModelOption_IsTrimmedAndCarriedOver()
        => Assert.Equal("jev-1.13.0", Resolve(new SystemOneClientOptions { ApiKey = "k", Model = "  jev-1.13.0\n" }).Model);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void BlankModel_Throws(string model)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Resolve(new SystemOneClientOptions { ApiKey = "k", Model = model }));

        Assert.Equal("options", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidOptionsCases.All), MemberType = typeof(InvalidOptionsCases))]
    public void InvalidOptions_ThrowTheirException(string name)
    {
        var invalid = InvalidOptionsCases.Get(name);

        var exception = Record.Exception(() => SystemOneClientSettings.Resolve(invalid.Options(), invalid.Lookup));

        Assert.NotNull(exception);
        Assert.IsType(invalid.Exception, exception);
    }

    private static SystemOneClientSettings Resolve(SystemOneClientOptions? options, params (string Name, string Value)[] environment)
        => SystemOneClientSettings.Resolve(
            options,
            name => Array.Find(environment, variable => string.Equals(variable.Name, name, StringComparison.Ordinal)).Value);
}
