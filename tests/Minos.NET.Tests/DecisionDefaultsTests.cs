using Minos.SystemOne;

namespace Minos.Tests;

public sealed class DecisionDefaultsTests
{
    [Fact]
    public void TypeSafeBaseAddress_IsTypeSafeApiRoot()
        => Assert.Equal(new Uri("https://api.typesafe.ai/"), SystemOneDefaults.TypeSafeBaseAddress);

    [Fact]
    public void OpenRouterBaseAddress_IsOpenRouterApiRoot()
        => Assert.Equal(new Uri("https://openrouter.ai/api/"), SystemOneDefaults.OpenRouterBaseAddress);

    [Fact]
    public void TypeSafeApiKeyEnvironmentVariable_MatchesOfficialSdks()
        => Assert.Equal("TYPESAFE_API_KEY", SystemOneDefaults.TypeSafeApiKeyEnvironmentVariable);

    [Fact]
    public void OpenRouterApiKeyEnvironmentVariable_IsOpenRouterConvention()
        => Assert.Equal("OPENROUTER_API_KEY", SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable);

    [Fact]
    public void TypeSafeBaseAddressEnvironmentVariable_MatchesOfficialSdks()
        => Assert.Equal("TYPESAFE_BASE_URL", SystemOneDefaults.TypeSafeBaseAddressEnvironmentVariable);

    [Fact]
    public void Model_DefaultsToLatestAlias()
        => Assert.Equal("jev-latest", SystemOneDefaults.Model);
}
