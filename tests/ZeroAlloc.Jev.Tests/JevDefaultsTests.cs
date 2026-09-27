namespace ZeroAlloc.Jev.Tests;

public sealed class JevDefaultsTests
{
    [Fact]
    public void TypeSafeBaseAddress_IsTypeSafeApiRoot()
        => Assert.Equal(new Uri("https://api.typesafe.ai/"), JevDefaults.TypeSafeBaseAddress);

    [Fact]
    public void OpenRouterBaseAddress_IsOpenRouterApiRoot()
        => Assert.Equal(new Uri("https://openrouter.ai/api/"), JevDefaults.OpenRouterBaseAddress);

    [Fact]
    public void ApiKeyEnvironmentVariable_MatchesOfficialSdks()
        => Assert.Equal("TYPESAFE_API_KEY", JevDefaults.ApiKeyEnvironmentVariable);

    [Fact]
    public void OpenRouterApiKeyEnvironmentVariable_IsOpenRouterConvention()
        => Assert.Equal("OPENROUTER_API_KEY", JevDefaults.OpenRouterApiKeyEnvironmentVariable);

    [Fact]
    public void BaseAddressEnvironmentVariable_MatchesOfficialSdks()
        => Assert.Equal("TYPESAFE_BASE_URL", JevDefaults.BaseAddressEnvironmentVariable);

    [Fact]
    public void Model_DefaultsToLatestAlias()
        => Assert.Equal("jev-latest", JevDefaults.Model);
}
