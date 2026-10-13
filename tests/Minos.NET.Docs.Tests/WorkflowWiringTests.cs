namespace Minos.Docs.Tests;

// The release path runs for real only on release day, so its wiring is pinned here, where a drift fails a push: the
// commands, the guards and the conditions of the jobs that pack, rehearse and publish.
public sealed class WorkflowWiringTests
{
    private static readonly string Ci = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "ci.yml")).ReplaceLineEndings("\n");

    [Fact]
    public void PackValidate_PacksTheReleaseBuild_InspectsIt_AndRehearsesThePush()
    {
        var job = Job(Ci, "pack-validate");

        Assert.Contains("dotnet pack Minos.NET.slnx -c Release -p:MinosRelease=true -o artifacts/packages", job, StringComparison.Ordinal);
        Assert.Contains("python3 -m unittest discover -s tools/release -p \"test_inspect_packages.py\"", job, StringComparison.Ordinal);
        Assert.Contains("python3 tools/release/inspect_packages.py artifacts/packages \"$PACKAGE_VERSION\"", job, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget push \"artifacts/packages/*.nupkg\" --source \"$feed\" --skip-duplicate", job, StringComparison.Ordinal);
    }

    // The text of one job: from its "  <name>:" line to the next job at the same indent, or the end of the file.
    internal static string Job(string workflow, string name)
    {
        var start = workflow.IndexOf("\n  " + name + ":\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The workflow has no job '{name}'.");
        var next = System.Text.RegularExpressions.Regex.Match(workflow[(start + 1)..], @"\n  [A-Za-z][A-Za-z0-9_-]*:\n", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
        return next.Success ? workflow.Substring(start, next.Index + 1) : workflow[start..];
    }
}
