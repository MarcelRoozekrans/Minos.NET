using Minos.OpenAI.Transport;
using Minos.Transport;

namespace Minos.OpenAI.Tests;

public sealed class OpenAIClientSettingsTests
{
    private const string MissingKey =
        "An OpenAI API key is required: set OpenAIDecisionClientOptions.ApiKey or the OPENAI_API_KEY environment variable.";

    [Fact]
    public void Defaults_ReadTheKeyFromTheEnvironment()
    {
        var settings = Resolve(null, ("OPENAI_API_KEY", "env-key"));

        Assert.Equal("env-key", settings.ApiKey);
        Assert.Equal("Bearer env-key", settings.Authorization);
        Assert.Equal(new Uri("https://api.openai.com/"), settings.BaseAddress);
        Assert.Equal("v1/decisions", OpenAIClientSettings.Path);
        Assert.Equal("gpt-6-luna", settings.Model);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.Timeout);
        Assert.Equal(2, settings.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(500), settings.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.MaxRetryDelay);
        Assert.True(settings.Jitter);
        Assert.True(settings.UseStandardPipeline);
    }

    [Fact]
    public void Defaults_MatchOpenAIDefaults()
    {
        Assert.Equal("OPENAI_API_KEY", OpenAIDefaults.ApiKeyEnvironmentVariable);
        Assert.Equal("OPENAI_BASE_URL", OpenAIDefaults.BaseAddressEnvironmentVariable);
        Assert.Equal("gpt-6-luna", OpenAIDefaults.Model);
        Assert.Equal(new Uri("https://api.openai.com/"), OpenAIDefaults.BaseAddress);
    }

    [Fact]
    public void ExplicitApiKey_WinsOverEnvironment_AndIsTrimmed()
    {
        var settings = Resolve(new OpenAIDecisionClientOptions { ApiKey = " explicit\n" }, ("OPENAI_API_KEY", "env-key"));

        Assert.Equal("explicit", settings.ApiKey);
        Assert.Equal("Bearer explicit", settings.Authorization);
    }

    [Fact]
    public void BlankApiKey_FallsBackToEnvironment()
        => Assert.Equal("env-key", Resolve(new OpenAIDecisionClientOptions { ApiKey = "  " }, ("OPENAI_API_KEY", "env-key")).ApiKey);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", " \t")]
    public void MissingOrBlankApiKey_Throws(string? option, string? environment)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Resolve(new OpenAIDecisionClientOptions { ApiKey = option }, environment is null ? [] : [("OPENAI_API_KEY", environment)]));

        Assert.Equal(MissingKey, exception.Message);
    }

    [Fact]
    public void ApiKeyOption_WithControlCharacter_Throws_WithoutTheKey()
    {
        const string key = "ab\u0001cd";

        var exception = Assert.Throws<ArgumentException>(() => Resolve(new OpenAIDecisionClientOptions { ApiKey = key }));

        Assert.StartsWith("The API key contains a control character.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(key, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeyEnvironment_WithControlCharacter_Throws_NamingTheVariable()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(null, ("OPENAI_API_KEY", "ab\u0001cd")));

        Assert.Equal("The OPENAI_API_KEY environment variable contains a control character.", exception.Message);
    }

    [Fact]
    public void BaseAddressOption_WinsOverEnvironment()
        => Assert.Equal(
            new Uri("http://option.local/"),
            Resolve(Keyed(o => o.BaseAddress = new Uri("http://option.local/")), ("OPENAI_BASE_URL", "http://env.local/")).BaseAddress);

    [Fact]
    public void BaseAddressEnvironment_WinsOverTheDefault_AndGetsATrailingSlash()
        => Assert.Equal(new Uri("http://env.local/v1proxy/"), Resolve(Keyed(), ("OPENAI_BASE_URL", "http://env.local/v1proxy")).BaseAddress);

    [Fact]
    public void BaseAddressOption_GetsATrailingSlash()
        => Assert.Equal(
            new Uri("http://localhost:5000/api/"),
            Resolve(Keyed(o => o.BaseAddress = new Uri("http://localhost:5000/api"))).BaseAddress);

    [Theory]
    [InlineData("v1", UriKind.Relative, "The base address must be an absolute URI.")]
    [InlineData("http://host/api?key=value", UriKind.Absolute, "The base address must not contain a query or fragment.")]
    [InlineData("http://host/api#x", UriKind.Absolute, "The base address must not contain a query or fragment.")]
    [InlineData("ftp://host/api/", UriKind.Absolute, "The base address must use the http or https scheme.")]
    public void InvalidBaseAddressOption_Throws(string address, UriKind kind, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => Resolve(Keyed(o => o.BaseAddress = new Uri(address, kind))));

        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a url", "The OPENAI_BASE_URL environment variable is not an absolute URI.")]
    [InlineData("http://host/api?key=value", "The OPENAI_BASE_URL environment variable must not contain a query or fragment.")]
    [InlineData("ftp://host/api/", "The OPENAI_BASE_URL environment variable must use the http or https scheme.")]
    public void InvalidBaseAddressEnvironment_Throws_NamingTheVariable(string value, string message)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Resolve(Keyed(), ("OPENAI_BASE_URL", value)));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void ModelOption_IsTrimmedAndCarriedOver()
        => Assert.Equal("gpt-6", Resolve(Keyed(o => o.Model = "  gpt-6\n")).Model);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void BlankModel_Throws(string model)
    {
        var exception = Assert.Throws<ArgumentException>(() => Resolve(Keyed(o => o.Model = model)));

        Assert.StartsWith("The model must not be blank.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RetryOptions_AreCarriedOver()
    {
        var settings = Resolve(Keyed(o =>
        {
            o.MaxRetries = 5;
            o.InitialBackoff = TimeSpan.FromSeconds(1);
            o.MaxRetryDelay = TimeSpan.FromSeconds(10);
            o.Jitter = false;
            o.UseStandardPipeline = false;
            o.Timeout = Timeout.InfiniteTimeSpan;
        }));

        Assert.Equal(5, settings.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.MaxRetryDelay);
        Assert.False(settings.Jitter);
        Assert.False(settings.UseStandardPipeline);
        Assert.Equal(Timeout.InfiniteTimeSpan, settings.Timeout);
    }

    [Theory]
    [InlineData(-1, 500, 30000, "MaxRetries must be between 0 and 10.")]
    [InlineData(11, 500, 30000, "MaxRetries must be between 0 and 10.")]
    [InlineData(2, 0, 30000, "InitialBackoff must be positive and at most int.MaxValue milliseconds.")]
    [InlineData(2, 2000, 1000, "MaxRetryDelay must be at least InitialBackoff and at most int.MaxValue milliseconds.")]
    public void InvalidRetrySettings_Throw(int maxRetries, int initialMs, int maxDelayMs, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => Resolve(Keyed(o =>
        {
            o.MaxRetries = maxRetries;
            o.InitialBackoff = TimeSpan.FromMilliseconds(initialMs);
            o.MaxRetryDelay = TimeSpan.FromMilliseconds(maxDelayMs);
        })));

        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void NonPositiveTimeout_Throws(int milliseconds)
    {
        var exception = Assert.Throws<ArgumentException>(() => Resolve(Keyed(o => o.Timeout = TimeSpan.FromMilliseconds(milliseconds))));

        Assert.StartsWith("The time-out must be positive.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TimeoutBeyondIntMilliseconds_Throws()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Resolve(Keyed(o => o.Timeout = TimeSpan.FromMilliseconds(int.MaxValue + 1.0))));

        Assert.StartsWith("The time-out must not exceed 2147483647 milliseconds.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Capabilities_DefaultToEveryKind_NoLimits_Images_NoModelListing()
    {
        var capabilities = Resolve(Keyed()).Capabilities;

        Assert.Equal(new CapabilitySet(QuestionKinds.All, null, null, Images: true, ModelListing: false), capabilities);
    }

    [Fact]
    public void CapabilityOverrides_AreApplied()
    {
        var capabilities = Resolve(Keyed(o => o.Capabilities = new DecisionCapabilities { Images = false, MaxQuestions = 4 })).Capabilities;

        Assert.Equal(new CapabilitySet(QuestionKinds.All, 4, null, Images: false, ModelListing: false), capabilities);
    }

    [Theory]
    [InlineData(QuestionKinds.None, "Capabilities.QuestionKinds must allow at least one question kind.")]
    [InlineData((QuestionKinds)8, "Capabilities.QuestionKinds has an unknown flag.")]
    public void InvalidQuestionKinds_Throw(QuestionKinds kinds, string message)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Resolve(Keyed(o => o.Capabilities = new DecisionCapabilities { QuestionKinds = kinds })));

        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxQuestionsBelowOne_Throws(int max)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Resolve(Keyed(o => o.Capabilities = new DecisionCapabilities { MaxQuestions = max })));

        Assert.StartsWith("Capabilities.MaxQuestions must be at least 1.", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void MaxChoiceOptionsBelowTwo_Throws(int max)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Resolve(Keyed(o => o.Capabilities = new DecisionCapabilities { MaxChoiceOptions = max })));

        Assert.StartsWith("Capabilities.MaxChoiceOptions must be at least 2.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_DoesNotRevealTheApiKey()
        => Assert.DoesNotContain("secret-key", Resolve(new OpenAIDecisionClientOptions { ApiKey = "secret-key" }).ToString(), StringComparison.Ordinal);

    [Fact]
    public void Validate_RunsTheSameCheck()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new OpenAIDecisionClientOptions().Validate(_ => null));

        Assert.Equal(MissingKey, exception.Message);
    }

    [Fact]
    public void ResolveHttp_NeedsNoKey_AndAppliesTheSameAddressRules()
    {
        var http = OpenAIClientSettings.ResolveHttp(new OpenAIDecisionClientOptions { Timeout = TimeSpan.FromSeconds(5) }, Environment(("OPENAI_BASE_URL", "http://env.local/x")));

        Assert.Equal(new Uri("http://env.local/x/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(5), http.Timeout);
        Assert.Throws<ArgumentException>(
            () => OpenAIClientSettings.ResolveHttp(new OpenAIDecisionClientOptions { Timeout = TimeSpan.Zero }, _ => null));
    }

    private static OpenAIDecisionClientOptions Keyed(Action<OpenAIDecisionClientOptions>? configure = null)
    {
        var options = new OpenAIDecisionClientOptions { ApiKey = "k" };
        configure?.Invoke(options);
        return options;
    }

    private static OpenAIClientSettings Resolve(OpenAIDecisionClientOptions? options, params (string Name, string Value)[] environment)
        => OpenAIClientSettings.Resolve(options, Environment(environment));

    private static Func<string, string?> Environment(params (string Name, string Value)[] environment)
        => name =>
        {
            foreach (var (variable, value) in environment)
            {
                if (string.Equals(variable, name, StringComparison.Ordinal))
                {
                    return value;
                }
            }

            return null;
        };
}
