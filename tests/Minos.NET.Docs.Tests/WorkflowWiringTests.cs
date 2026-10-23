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
        Assert.Contains("\"$PACKAGE_VERSION\" | \"$PACKAGE_VERSION+\"*) ;;", job, StringComparison.Ordinal);
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
        Assert.DoesNotContain("if-no-files-found: ignore", job, StringComparison.Ordinal);
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

    private static readonly string Calibrate = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "calibrate.yml")).ReplaceLineEndings("\n");

    private static readonly string LocalSystemOne = File.ReadAllText(Path.Combine(PublishedPages.Root, ".github", "workflows", "local-systemone.yml")).ReplaceLineEndings("\n");

    // The runner signals only the step's shell, so the shell passes the stop on to minos as SIGTERM and waits for it to
    // save results.partial.jsonl. A background job ignores SIGINT, so SIGINT would not reach it.
    private static void AssertForwardsTheStopToMinos(string job)
    {
        Assert.Contains("status=0\n          pid=\n", job, StringComparison.Ordinal);
        Assert.Contains("trap 'trap \"\" INT TERM; if [ -n \"$pid\" ]; then kill -TERM \"$pid\" 2>/dev/null; wait \"$pid\"; fi; exit 130' INT TERM\n", job, StringComparison.Ordinal);
        Assert.Contains(" & pid=$!\n            wait \"$pid\" || status=1\n            pid=\n          done\n", job, StringComparison.Ordinal);
    }

    [Fact]
    public void Calibrate_RunsOnlyOnDispatch_WithTheLiveApiKeys()
    {
        Assert.Contains("on:\n  workflow_dispatch:\n", Calibrate, StringComparison.Ordinal);
        Assert.DoesNotContain("push:", Calibrate, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request:", Calibrate, StringComparison.Ordinal);
        Assert.DoesNotContain("schedule:", Calibrate, StringComparison.Ordinal);

        var job = Job(Calibrate, "calibrate");
        Assert.Contains("environment: live-api", job, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-latest", job, StringComparison.Ordinal);
        Assert.Contains("timeout-minutes: 150", job, StringComparison.Ordinal);
        Assert.Contains("permissions:\n      contents: read\n", job, StringComparison.Ordinal);
        Assert.Contains("          persist-credentials: false\n", job, StringComparison.Ordinal);
        Assert.Contains("TYPESAFE_API_KEY: ${{ inputs.provider == 'typesafe' && secrets.TYPESAFE_API_KEY || '' }}", job, StringComparison.Ordinal);
        Assert.Contains("OPENROUTER_API_KEY: ${{ inputs.provider == 'clef' && secrets.OPENROUTER_API_KEY || '' }}", job, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ inputs.", RunScripts(Calibrate), StringComparison.Ordinal);

        Assert.Contains("      provider:\n        description: Which hosted provider to calibrate\n        type: choice\n        required: true\n        options:\n          - typesafe\n          - clef\n", Calibrate, StringComparison.Ordinal);
        Assert.Contains("      set:\n        description: Which dataset to run\n        type: choice\n        default: both\n        options:\n          - both\n          - urgency\n          - banking77\n", Calibrate, StringComparison.Ordinal);
    }

    [Fact]
    public void Calibrate_InstallsTheReleasedTool_AndRunsEachSetOneRequestAtATime()
    {
        var job = Job(Calibrate, "calibrate");

        Assert.Contains("dotnet tool install Minos.NET.Tool --version 1.4.0 --tool-path \"$RUNNER_TEMP/minos-tool\"", job, StringComparison.Ordinal);
        Assert.Contains("typesafe) echo \"section=TypeSafe\" >> \"$GITHUB_OUTPUT\"; echo \"slug=typesafe\" >> \"$GITHUB_OUTPUT\" ;;", job, StringComparison.Ordinal);
        Assert.Contains("clef) echo \"section=Clef\" >> \"$GITHUB_OUTPUT\"; echo \"slug=clef-flash\" >> \"$GITHUB_OUTPUT\" ;;", job, StringComparison.Ordinal);
        Assert.Contains("\"$RUNNER_TEMP/minos-tool/minos\" calibrate --dataset \"$dataset\" --config docs/reports/minos.json --provider \"$SECTION\" --parallel 1 --out \"$RUNNER_TEMP/out/$SLUG-$set\" & pid=$!\n", job, StringComparison.Ordinal);
        AssertForwardsTheStopToMinos(job);
        Assert.Contains("datasets/banking77/banking77-minos-v1.jsonl", job, StringComparison.Ordinal);
        Assert.Contains("datasets/banking77/banking77-urgency-v1.jsonl", job, StringComparison.Ordinal);
        Assert.DoesNotContain("--prices", job, StringComparison.Ordinal);
        Assert.Contains("both|urgency|banking77) ;;\n            *) echo \"::error::Unknown set '$SET'\"; exit 1 ;;", job, StringComparison.Ordinal);
        Assert.Contains("if [ \"$SET\" != both ] && [ \"$SET\" != \"$set\" ]; then continue; fi", job, StringComparison.Ordinal);
        Assert.Contains("|| status=1", job, StringComparison.Ordinal);
        Assert.Contains("exit \"$status\"", job, StringComparison.Ordinal);

        Assert.Contains("name: calibration-${{ steps.map.outputs.slug }}-urgency\n", job, StringComparison.Ordinal);
        Assert.Contains("name: calibration-${{ steps.map.outputs.slug }}-banking77\n", job, StringComparison.Ordinal);
        Assert.Contains("path: ${{ runner.temp }}/out/${{ steps.map.outputs.slug }}-urgency\n", job, StringComparison.Ordinal);
        Assert.Contains("path: ${{ runner.temp }}/out/${{ steps.map.outputs.slug }}-banking77\n", job, StringComparison.Ordinal);
        Assert.Contains("if: always() && (inputs.set == 'both' || inputs.set == 'urgency')", job, StringComparison.Ordinal);
        Assert.Contains("if: always() && (inputs.set == 'both' || inputs.set == 'banking77')", job, StringComparison.Ordinal);
        Assert.DoesNotContain("if-no-files-found: ignore", job, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(job, "if-no-files-found: warn", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void LocalSystemOne_CalibratesOnlyOnDispatch_AgainstTheLocalServer()
    {
        var job = Job(LocalSystemOne, "calibrate");

        Assert.Contains("if: github.event_name == 'workflow_dispatch'", job, StringComparison.Ordinal);
        Assert.Contains("timeout-minutes: 120", job, StringComparison.Ordinal);
        Assert.Contains("pip install --extra-index-url https://download.pytorch.org/whl/cpu ", job, StringComparison.Ordinal);
        Assert.Contains(" serve Mapika/decider-2b-GGUF:Q4_K_M --port 8000 --no-browser", job, StringComparison.Ordinal);
        Assert.Contains("dotnet tool install Minos.NET.Tool --version 1.4.0 --tool-path \"$RUNNER_TEMP/minos-tool\"", job, StringComparison.Ordinal);
        Assert.Contains("--config docs/reports/minos.json --provider Local --parallel 1 --out \"$RUNNER_TEMP/out/local-$set\" & pid=$!\n", job, StringComparison.Ordinal);
        AssertForwardsTheStopToMinos(job);
        Assert.Contains("datasets/banking77/banking77-minos-v1.jsonl", job, StringComparison.Ordinal);
        Assert.Contains("datasets/banking77/banking77-urgency-v1.jsonl", job, StringComparison.Ordinal);
        Assert.Contains("name: calibration-local-urgency\n", job, StringComparison.Ordinal);
        Assert.Contains("name: calibration-local-banking77\n", job, StringComparison.Ordinal);
        Assert.Contains("if: always()", job, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", job, StringComparison.Ordinal);
        Assert.DoesNotContain("if-no-files-found: ignore", job, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalSystemOne_CalibrateJob_TakesASetInput_ValidatesIt_AndGatesTheUploads()
    {
        Assert.Contains("  workflow_dispatch:\n    inputs:\n      set:\n        description: Which dataset to run\n        type: choice\n        default: both\n        options:\n          - both\n          - urgency\n          - banking77\n", LocalSystemOne, StringComparison.Ordinal);

        var job = Job(LocalSystemOne, "calibrate");
        Assert.Contains("SET: ${{ inputs.set }}", job, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ inputs.", RunScripts(LocalSystemOne), StringComparison.Ordinal);
        Assert.Contains("both|urgency|banking77) ;;\n            *) echo \"::error::Unknown set '$SET'\"; exit 1 ;;", job, StringComparison.Ordinal);
        Assert.Contains("if [ \"$SET\" != both ] && [ \"$SET\" != \"$set\" ]; then continue; fi", job, StringComparison.Ordinal);
        Assert.Contains("|| status=1", job, StringComparison.Ordinal);
        Assert.Contains("exit \"$status\"", job, StringComparison.Ordinal);
        Assert.Contains("if: always() && (inputs.set == 'both' || inputs.set == 'urgency')", job, StringComparison.Ordinal);
        Assert.Contains("if: always() && (inputs.set == 'both' || inputs.set == 'banking77')", job, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(job, "if-no-files-found: warn", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void LocalSystemOne_KeepsItsPushJob()
    {
        var job = Job(LocalSystemOne, "local-systemone");

        Assert.Contains("timeout-minutes: 30", job, StringComparison.Ordinal);
        Assert.DoesNotContain("minos calibrate", job, StringComparison.Ordinal);
        Assert.DoesNotContain(" calibrate ", job, StringComparison.Ordinal);
        Assert.DoesNotContain("inputs.", job, StringComparison.Ordinal);
    }

    // The text of every run: script in a workflow, so a test can tell script text from with: and env: values.
    private static string RunScripts(string workflow)
    {
        var lines = workflow.Split('\n');
        var scripts = new System.Text.StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("run:", StringComparison.Ordinal)) continue;
            var indent = lines[i].Length - trimmed.Length;
            scripts.AppendLine(trimmed);
            while (i + 1 < lines.Length && (lines[i + 1].Trim().Length == 0 || lines[i + 1].Length - lines[i + 1].TrimStart().Length > indent))
            {
                scripts.AppendLine(lines[++i]);
            }
        }
        return scripts.ToString();
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
