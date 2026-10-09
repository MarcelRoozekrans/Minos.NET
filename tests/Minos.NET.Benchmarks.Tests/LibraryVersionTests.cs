using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Minos.Benchmarks.Compare.Adapters;

namespace Minos.Benchmarks.Tests;

/// <summary>The versions the result file reports for each library.</summary>
public sealed partial class LibraryVersionTests
{
    [Theory]
    [InlineData("0.3.3+1a2b3c4d5e", "0.3.3", "1a2b3c4d5e")]
    [InlineData("0.3.3", "0.3.3", null)]
    [InlineData("1.0.0-beta.1+abc", "1.0.0-beta.1", "abc")]
    [InlineData("0.3.3+", "0.3.3", null)]
    public void An_informational_version_splits_into_the_version_and_the_commit(string informational, string version, string? commit)
    {
        Assert.Equal((version, commit), LibraryVersion.Split(informational));
    }

    [Fact]
    public void ZeroAlloc_Jev_reports_its_local_version_and_short_commit_when_one_is_recorded()
    {
        using var adapter = new JevAdapter(new Uri("http://127.0.0.1:1/"));
        var informational = typeof(JevClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        // A build that is not a release is versioned as the manifest's last release with the -local suffix, so the
        // table shows a branch build as one. A build without git metadata, such as one from a source tarball, records
        // no commit, and then reports the version alone.
        var release = ManifestRelease();
        var dash = release.IndexOf('-', StringComparison.Ordinal);
        var local = dash < 0 ? release + "-local" : release[..dash] + "-0.local." + release[(dash + 1)..];
        var (_, commit) = LibraryVersion.Split(informational);
        if (commit is null)
        {
            Assert.Equal(local, adapter.Version);
        }
        else
        {
            Assert.StartsWith(local + "+", adapter.Version, StringComparison.Ordinal);
            Assert.Matches(SourceBuildVersion(), adapter.Version);
        }
    }

    [Fact]
    public void A_packaged_library_reports_its_version_without_a_commit()
    {
        using var adapter = ClientAdapters.Create(ClientAdapters.JevSharp, new Uri("http://127.0.0.1:1/"));

        Assert.DoesNotContain("+", adapter.Version, StringComparison.Ordinal);
    }

    // The manifest's version, then the short commit the SDK records in the informational version.
    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.-]+)?\\+[0-9a-f]{7}$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SourceBuildVersion();

    private static string ManifestRelease()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".release-please-manifest.json");
            if (File.Exists(path))
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(path));
                return manifest.RootElement.GetProperty(".").GetString()!;
            }
        }

        throw new InvalidOperationException("Could not find .release-please-manifest.json above the test's directory.");
    }
}
