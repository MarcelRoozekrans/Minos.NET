using Minos.Conformance;

namespace Minos.Docs.Tests;

// Keeps the Conformance page in step with the suite: its probe, check and source tables list the suite's own, in order,
// and it names every header a fixture keeps. It reads no cell's wording, so a cell can be reworded without breaking it.
public sealed class ConformancePageTests
{
    private const string Page = "conformance.md";

    [Fact]
    public void TheProbeTable_IsTheSuitesProbes_InOrder()
        => Assert.Equal(ConformanceProbe.All.Select(probe => probe.Id), PageTables.Rows(Page, "The probes").Select(row => PageTables.Code(row[0])));

    [Fact]
    public void TheCheckTable_IsTheSuitesChecks_InOrder()
        => Assert.Equal(ConformanceChecks.Ids, PageTables.Rows(Page, "The checks").Select(row => PageTables.Code(row[0])));

    [Fact]
    public void TheSourceTable_IsTheFixtureSources()
        => Assert.Equal(
            [ConformanceFixture.RecordedSource, ConformanceFixture.TestsSource, ConformanceFixture.GuideSource],
            PageTables.Rows(Page, "The fixtures").Select(row => PageTables.Code(row[0])));

    [Fact]
    public void ThePage_NamesEveryHeaderAFixtureKeeps()
    {
        var text = PageTables.Text(Page);

        Assert.All(ExchangeCapture.RecordedHeaders, header => Assert.Contains("`" + header + "`", text, StringComparison.Ordinal));
    }
}
