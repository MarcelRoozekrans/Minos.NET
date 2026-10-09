namespace Minos.Tests;

public sealed class DecisionDefaultsTests
{
    [Fact]
    public void TypeSafeBaseAddress_IsTypeSafeApiRoot()
        => Assert.Equal(new Uri("https://api.typesafe.ai/"), DecisionDefaults.TypeSafeBaseAddress);

    [Fact]
    public void OpenRouterBaseAddress_IsOpenRouterApiRoot()
        => Assert.Equal(new Uri("https://openrouter.ai/api/"), DecisionDefaults.OpenRouterBaseAddress);

    [Fact]
    public void ApiKeyEnvironmentVariable_MatchesOfficialSdks()
        => Assert.Equal("TYPESAFE_API_KEY", DecisionDefaults.ApiKeyEnvironmentVariable);

    [Fact]
    public void OpenRouterApiKeyEnvironmentVariable_IsOpenRouterConvention()
        => Assert.Equal("OPENROUTER_API_KEY", DecisionDefaults.OpenRouterApiKeyEnvironmentVariable);

    [Fact]
    public void BaseAddressEnvironmentVariable_MatchesOfficialSdks()
        => Assert.Equal("TYPESAFE_BASE_URL", DecisionDefaults.BaseAddressEnvironmentVariable);

    [Fact]
    public void Model_DefaultsToLatestAlias()
        => Assert.Equal("jev-latest", DecisionDefaults.Model);
}
