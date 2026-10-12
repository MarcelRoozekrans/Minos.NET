using ZeroAlloc.TestHelpers;

namespace Minos.Conformance.Tests;

public sealed class ReportTests
{
    private static ConformanceReport Sample() => new()
    {
        Target = "typesafe",
        Wire = ConformanceWire.SystemOne,
        Model = "jev-latest",
        ModelRule = ModelRule.TypeSafe.Description,
        Mode = ConformanceMode.Replay,
        FixtureSource = ConformanceFixture.TestsSource,
        FixtureRecorded = null,
        Probes =
        [
            new ProbeReport("noul", null,
            [
                CheckResult.Passed("S1", "One answer for each question asked, 1 in all."),
                CheckResult.Skip("D2", "The probe asks no Choice or Score question."),
                CheckResult.Failed("R2", "gpt-6-luna does not fit jev-latest. A | in a detail is escaped."),
            ]),
            new ProbeReport("invalid", "The fixture skips it: no body yet.", [CheckResult.Skip("E1", "The fixture skips it: no body yet.")]),
        ],
        RunChecks = [CheckResult.Skip("E4", "No 429 was met during the run.")],
    };

    [Fact]
    public void Counts_AndTheNamedChecks()
    {
        var report = Sample();

        Assert.Equal((1, 1, 3), (report.Passed, report.Failed, report.Skipped));
        Assert.Equal(["noul:R2"], report.FailedChecks);
        Assert.Equal(["noul:D2", "invalid:E1", "run:E4"], report.SkippedChecks);
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var json = Sample().ToJson();

        var read = ConformanceReport.FromJson(json);

        Assert.Equal(json, read.ToJson());
        Assert.Equal(Sample().Probes[0].Checks, read.Probes[0].Checks);
        Assert.Contains("\"outcome\": \"Skipped\"", json, StringComparison.Ordinal);
        Assert.Contains("\"mode\": \"Replay\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("fixtureRecorded", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_MatchesTheSnapshot() => TextSnapshot.VerifyText(Sample().ToMarkdown(), "md");
}
