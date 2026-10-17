using Minos.SystemOne;
using Minos.Tool;

namespace Minos.Docs.Tests;

// Pins docs/reports/minos.json, the config every calibration run passes with --config: the three sections, the
// model each names, and the zero data retention request on the Clef section. Also pins that the reports stay off the site.
// Serialized with the other test classes that set or read the key environment variables.
[Collection("key environment")]
public sealed class RunConfigTests
{
    private static readonly string ConfigPath = Path.Combine(PublishedPages.Root, "docs", "reports", "minos.json");

    [Theory]
    [InlineData("TypeSafe", SystemOneProvider.TypeSafe, "jev-latest")]
    [InlineData("Clef", SystemOneProvider.OpenRouter, "cloudflare/clef-flash")]
    [InlineData("Local", SystemOneProvider.Custom, "Mapika/decider-2b-GGUF:Q4_K_M")]
    public void TheRunConfig_Resolves(string section, SystemOneProvider provider, string model)
    {
        using var environment = new KeyEnvironment("typesafe-key", "openrouter-key");

        var resolved = ToolConfiguration.Resolve(ConfigPath, section, null);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : string.Empty);
        Assert.Equal(section, resolved.Value.Name);
        Assert.Equal(provider, resolved.Value.Options.Provider);
        Assert.Equal(model, resolved.Value.Options.Model);
        Assert.Equal(2, resolved.Value.Options.MaxRetries);
    }

    [Fact]
    public void TheRunConfig_NamesTypeSafeAsActive()
    {
        using var environment = new KeyEnvironment("typesafe-key", "openrouter-key");

        var resolved = ToolConfiguration.Resolve(ConfigPath, null, null);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : string.Empty);
        Assert.Equal("TypeSafe", resolved.Value.Name);
    }

    [Fact]
    public void TheLocalSection_PointsAtTheLocalServer()
    {
        using var environment = new KeyEnvironment("typesafe-key", "openrouter-key");

        var resolved = ToolConfiguration.Resolve(ConfigPath, "Local", null);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : string.Empty);
        Assert.Equal(new Uri("http://127.0.0.1:8000/"), resolved.Value.Options.BaseAddress);
    }

    [Fact]
    public void TheClefSection_AsksForZeroDataRetention()
    {
        using var environment = new KeyEnvironment("typesafe-key", "openrouter-key");

        var resolved = ToolConfiguration.Resolve(ConfigPath, "Clef", null);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : string.Empty);
        Assert.True(resolved.Value.Options.ProviderPreferences?.ZeroDataRetention);
    }

    [Theory]
    [InlineData("TypeSafe")]
    [InlineData("Local")]
    public void TheOtherSections_SetNoProviderPreferences(string section)
    {
        using var environment = new KeyEnvironment("typesafe-key", "openrouter-key");

        var resolved = ToolConfiguration.Resolve(ConfigPath, section, null);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : string.Empty);
        Assert.Null(resolved.Value.Options.ProviderPreferences);
    }

    [Fact]
    public void TheRunConfig_HoldsNoKey()
    {
        Assert.DoesNotContain("ApiKey", File.ReadAllText(ConfigPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reports_AreNotPublished()
    {
        Assert.True(PublishedPages.IsUnpublished("reports/typesafe/banking77/report.md"));
        Assert.Contains("'**/reports/**'", File.ReadAllText(Path.Combine(PublishedPages.Root, "docusaurus.config.ts")), StringComparison.Ordinal);
    }
}
