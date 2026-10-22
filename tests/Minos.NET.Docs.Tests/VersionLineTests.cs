using System.Text.Json;

namespace Minos.Docs.Tests;

// The guide names the release it describes. release-please rewrites every line marked x-release-please-version in the
// files listed under extra-files, so each release PR updates it. These tests tie that line to the manifest, keep the
// marker out of the README, which nuget.org shows with raw HTML off, and pin the 1.0 status and the NuGet links.
public sealed class VersionLineTests
{
    private const string Marker = "x-release-please-version";

    private static readonly string[] Packages =
        ["Minos.NET", "Minos.NET.SystemOne", "Minos.NET.OpenAI", "Minos.NET.DependencyInjection", "Minos.NET.Caching", "Minos.NET.RateLimiting", "Minos.NET.Evaluation", "Minos.NET.Recording", "Minos.NET.Tool"];

    [Fact]
    public void GettingStarted_NamesTheReleaseInTheManifest()
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", "getting-started.md"));

        var marked = Assert.Single(lines, line => line.Contains(Marker, StringComparison.Ordinal));
        Assert.Equal($"This guide describes Minos.NET {Release()}. <!-- {Marker} -->", marked);
    }

    [Fact]
    public void ReleasePlease_UpdatesTheGuidesVersionLine()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, "release-please-config.json")));
        var extraFiles = config.RootElement.GetProperty("packages").GetProperty(".").GetProperty("extra-files");

        var entry = Assert.Single(extraFiles.EnumerateArray());
        Assert.Equal("generic", entry.GetProperty("type").GetString());
        Assert.Equal("docs/getting-started.md", entry.GetProperty("path").GetString());
    }

    [Fact]
    public void TheReadme_States1_0_AndLinksEveryPackageOnNuGet()
    {
        var readme = File.ReadAllText(Path.Combine(PublishedPages.Root, "README.md"));

        Assert.Contains("**Status:** version 1.0. The public API is stable and follows semantic versioning.", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("not yet published", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Marker, readme, StringComparison.Ordinal);
        Assert.All(Packages, package => Assert.Contains($"https://www.nuget.org/packages/{package}", readme, StringComparison.Ordinal));
    }

    [Fact]
    public void GettingStarted_NoLongerSaysThePackagesAreUnpublished()
    {
        var page = File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", "getting-started.md"));

        Assert.DoesNotContain("not yet published", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSite_LinksToNuGet_InTheNavbarAndTheFooter()
    {
        var config = File.ReadAllText(Path.Combine(PublishedPages.Root, "docusaurus.config.ts"));

        Assert.Equal(2, CountOf(config, "href: 'https://www.nuget.org/packages?q=Minos.NET'"));
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Release()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, ".release-please-manifest.json")));
        return manifest.RootElement.GetProperty(".").GetString()!;
    }
}
