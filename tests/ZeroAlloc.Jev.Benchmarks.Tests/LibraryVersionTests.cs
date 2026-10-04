using System.Text.RegularExpressions;
using ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

namespace ZeroAlloc.Jev.Benchmarks.Tests;

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
    public void ZeroAlloc_Jev_reports_its_released_version_and_short_commit()
    {
        using var adapter = new JevAdapter(new Uri("http://127.0.0.1:1/"));

        Assert.Matches(SourceBuildVersion(), adapter.Version);
        Assert.DoesNotContain("0.0.0", adapter.Version, StringComparison.Ordinal);
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
}
