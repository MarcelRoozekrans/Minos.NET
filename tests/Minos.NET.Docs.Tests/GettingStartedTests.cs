using System.Reflection;
using System.Text.Json;

namespace Minos.Docs.Tests;

public sealed class GettingStartedTests
{
    private const string TicketResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "is_urgent": { "type": "noul", "noul": 0.93 },
            "team": { "type": "choice", "choice": "billing", "probabilities": { "billing": 0.81, "technical": 0.15, "sales": 0.04 }, "confidence": 0.77 }
          },
          "usage": { "input_tokens": 120, "output_tokens": 12 }
        }
        """;

    [Fact]
    public async Task TriageAsync_ReadsTheTypedAnswers()
    {
        var (http, jev, requests) = CannedJev.Client(TicketResponse);
        using (http)
        using (jev)
        {
            var summary = await GettingStartedEvaluation.TriageAsync(jev, "Help! My payouts have been failing for 3 days.", CancellationToken.None);

            Assert.Equal("urgent, for Billing", summary);
        }

        Assert.Collection(requests, body => Assert.Contains("\"is_urgent\"", body, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TriageAsync_ReportsAFailureInsteadOfThrowing()
    {
        var (http, jev, _) = CannedJev.Client("this is not json");
        using (http)
        using (jev)
        {
            var summary = await GettingStartedEvaluation.TriageAsync(jev, "Anything.", CancellationToken.None);

            Assert.StartsWith("Jev failed, ", summary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TicketCheck_ExposesProbabilityAndConfidence()
    {
        var check = await CannedJev.EvaluateAsync<TicketCheck>(TicketResponse, "Help!");

        Assert.Equal(0.93, check.IsUrgent.Probability);
        Assert.Equal(SupportTeam.Billing, check.Team.Value);
        Assert.Equal(0.77, check.Team.Confidence);
    }

    // The install section's note on what the package brings: one Microsoft runtime dependency, and the dependencies'
    // analyzers, which flow whatever PrivateAssets says, NuGet/Home#6720. The pack tests check the packed nuspec.
    [Fact]
    public void TheInstallNote_MatchesThePackagesDependencies()
    {
        var project = File.ReadAllText(Path.Combine(PublishedPages.Root, "src", "Minos.NET", "Minos.NET.csproj"));
        var packages = File.ReadAllText(Path.Combine(PublishedPages.Root, "Directory.Packages.props"));
        var page = PageTables.Text("getting-started.md");

        Assert.Contains("<PackageReference Include=\"Microsoft.Extensions.Logging.Abstractions\" />", project, StringComparison.Ordinal);
        Assert.Contains("<PackageVersion Include=\"Microsoft.Extensions.Logging.Abstractions\" Version=\"10.0.0\" />", packages, StringComparison.Ordinal);
        Assert.Contains("`Microsoft.Extensions.Logging.Abstractions` 10.0.0 or later", page, StringComparison.Ordinal);

        Assert.Contains("NuGet still flows the analyzers", project, StringComparison.Ordinal);
        Assert.Contains("NuGet may still flow the analyzer", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ZeroAlloc.Validation\" PrivateAssets=\"analyzers;build;buildtransitive\" />", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"ZeroAlloc.Telemetry\" PrivateAssets=\"analyzers;build;buildtransitive\" />", project, StringComparison.Ordinal);
        Assert.Contains("`[Validate]` types, `[Instrument]` types or your own `[LoggerMessage]` methods", page, StringComparison.Ordinal);
    }

    // The local-pack section tells readers to add <release>-local, the version a normal build gets: the manifest's
    // release with the -local suffix that keeps it apart from the published package. Only a release build, which passes
    // -p:JevRelease=true, drops the suffix, and the test suite is never built that way.
    [Fact]
    public void TheLocalPackSection_UsesTheVersionALocalBuildGets()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, ".release-please-manifest.json")));
        var release = manifest.RootElement.GetProperty(".").GetString()!;
        var informational = typeof(JevClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var built = informational.Split('+')[0];
        var page = PageTables.Text("getting-started.md");

        var dash = release.IndexOf('-', StringComparison.Ordinal);
        Assert.Equal(dash < 0 ? release + "-local" : release[..dash] + "-0.local." + release[(dash + 1)..], built);
        Assert.Equal(new Version(release.Split('-')[0] + ".0"), typeof(JevClient).Assembly.GetName().Version);
        Assert.Contains("dotnet add package Minos.NET --version <release>-local", page, StringComparison.Ordinal);
        Assert.Contains("dotnet add package Minos.NET.DependencyInjection --version <release>-local", page, StringComparison.Ordinal);
        Assert.Contains("A local build is versioned as the last release with a `-local` suffix", page, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0-local", page, StringComparison.Ordinal);
    }
}
