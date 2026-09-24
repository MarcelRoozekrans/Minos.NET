namespace Jev.Net.Tests;

public sealed class JevDefaultsTests
{
    [Fact]
    public void BaseAddress_IsTypeSafeApiRoot()
    {
        Assert.Equal(new Uri("https://api.typesafe.ai/"), JevDefaults.BaseAddress);
    }

    [Fact]
    public void ApiKeyEnvironmentVariable_MatchesOfficialSdks()
    {
        Assert.Equal("TYPESAFE_API_KEY", JevDefaults.ApiKeyEnvironmentVariable);
    }

    [Fact]
    public void Model_DefaultsToLatestAlias()
    {
        Assert.Equal("jev-latest", JevDefaults.Model);
    }
}
