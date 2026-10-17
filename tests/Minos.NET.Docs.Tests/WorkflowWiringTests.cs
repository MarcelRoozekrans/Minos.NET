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

    [Fact]
    public void PackValidate_InstallsTheTool_FromTheLocalFeedConfig_AndChecksItsVersion()
    {
        var job = Job(Ci, "pack-validate");

        Assert.Contains("dotnet tool install Minos.NET.Tool --tool-path \"$RUNNER_TEMP/minos-tool\" --configfile \"$RUNNER_TEMP/consumer/nuget.config\" --version \"$PACKAGE_VERSION\"", job, StringComparison.Ordinal);
        Assert.Contains("\"$RUNNER_TEMP/minos-tool/minos\" --help", job, StringComparison.Ordinal);
        Assert.Contains("reported=$(\"$RUNNER_TEMP/minos-tool/minos\" --version)", job, StringComparison.Ordinal);
        Assert.Contains("\"$PACKAGE_VERSION\"*) ;;", job, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TestsTheBanking77SampleBuild()
    {
        var job = Job(Ci, "build");

        Assert.Contains("python3 -m unittest discover -s datasets/banking77 -p \"test_build.py\"", job, StringComparison.Ordinal);
    }

    private static readonly string ReleasePlease = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "release-please.yml")).ReplaceLineEndings("\n");

    [Fact]
    public void Ci_TakesAPublishInput_ThatDefaultsToFalse()
    {
        Assert.Contains(
            "      publish_to_nuget:\n        description: Push the release packages to nuget.org. Only takes effect on a v* tag; see RELEASING.md\n        type: boolean\n        default: false\n",
            Ci,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PublishNuget_RunsOnlyForADispatchOnATag_AfterEveryCheck()
    {
        var job = Job(Ci, "publish-nuget");

        Assert.Contains("if: github.event_name == 'workflow_dispatch' && inputs.publish_to_nuget && startsWith(github.ref, 'refs/tags/v')", job, StringComparison.Ordinal);
        Assert.Contains("needs: [build, aot-smoke, aot-surface, pack-validate]", job, StringComparison.Ordinal);
        Assert.Contains("id-token: write", job, StringComparison.Ordinal);
        Assert.Contains("contents: write", job, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishNuget_RefusesZeroXAndPrereleases_AndATagThatIsNotTheManifestVersion()
    {
        var job = Job(Ci, "publish-nuget");

        Assert.Contains("[[ \"$version\" =~ ^[0-9]+\\.[0-9]+\\.[0-9]+$ ]]", job, StringComparison.Ordinal);
        Assert.Contains("[[ \"$version\" == 0.* ]]", job, StringComparison.Ordinal);
        Assert.Contains("[ \"$GITHUB_REF_NAME\" != \"v$version\" ]", job, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishNuget_DoesNotPersistTheCheckoutCredentials()
    {
        var job = Job(Ci, "publish-nuget");

        Assert.Contains("          persist-credentials: false\n", job, StringComparison.Ordinal);
    }

    [Fact]
    public void PackValidate_AcceptsAPrereleaseManifestVersion()
    {
        var job = Job(Ci, "pack-validate");

        Assert.Contains("[[ \"$version\" =~ ^[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]", job, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishNuget_PacksAndInspectsAsPackValidateDoes_ThenPushesThroughTrustedPublishing()
    {
        var job = Job(Ci, "publish-nuget");

        Assert.Contains("dotnet pack Minos.NET.slnx -c Release -p:MinosRelease=true -o artifacts/packages", job, StringComparison.Ordinal);
        Assert.Contains("python3 tools/release/inspect_packages.py artifacts/packages \"$PACKAGE_VERSION\"", job, StringComparison.Ordinal);
        Assert.Contains("uses: NuGet/login@v1", job, StringComparison.Ordinal);
        Assert.Contains("user: ${{ vars.NUGET_USER }}", job, StringComparison.Ordinal);
        Assert.Contains("dotnet nuget push \"artifacts/packages/*.nupkg\" --source https://api.nuget.org/v3/index.json --api-key \"$NUGET_API_KEY\" $skip_duplicate", job, StringComparison.Ordinal);
        Assert.Contains("gh release upload \"$GITHUB_REF_NAME\" artifacts/packages/*.nupkg artifacts/packages/*.snupkg --clobber", job, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", job, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_TakesAResumeInput_ThatDefaultsToFalse()
    {
        Assert.Contains(
            "      resume:\n        description: Finish a publish that died partway, skipping the packages already on nuget.org; see RELEASING.md\n        type: boolean\n        default: false\n",
            Ci,
            StringComparison.Ordinal);
    }

    // A release run must fail when a version is already on nuget.org: with --skip-duplicate it would push nothing and still
    // go green. Only a resumed run skips the packages that already arrived.
    [Fact]
    public void PublishNuget_SkipsDuplicatesOnlyWhenResuming()
    {
        var job = Job(Ci, "publish-nuget");

        Assert.Contains("RESUME: ${{ inputs.resume }}", job, StringComparison.Ordinal);
        Assert.Contains("if [ \"$RESUME\" = \"true\" ]; then\n            skip_duplicate=\"--skip-duplicate\"", job, StringComparison.Ordinal);
        Assert.DoesNotContain("--api-key \"$NUGET_API_KEY\" --skip-duplicate", job, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePlease_DispatchesThePublish_WhenItCreatedARelease()
    {
        var job = Job(ReleasePlease, "publish");

        Assert.Contains("needs: release-please\n", job, StringComparison.Ordinal);
        Assert.Contains("if: ${{ needs.release-please.outputs.release_created == 'true' }}", job, StringComparison.Ordinal);
        Assert.Contains("TAG: ${{ needs.release-please.outputs.tag_name }}", job, StringComparison.Ordinal);
        Assert.Contains("gh workflow run ci.yml --repo \"$GITHUB_REPOSITORY\" --ref \"$TAG\" -f publish_to_nuget=true", job, StringComparison.Ordinal);
        Assert.Contains("actions: write", job, StringComparison.Ordinal);
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
