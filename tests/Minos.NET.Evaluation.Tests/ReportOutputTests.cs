using System.Globalization;
using System.Text.Json;
using ZeroAlloc.TestHelpers;
using static Minos.Evaluation.Tests.ReportFixture;

namespace Minos.Evaluation.Tests;

public sealed class ReportOutputTests
{
    [Fact]
    public void Json_HasKindVersionHeaderAndFullPrecision()
    {
        var report = AllKindsReport();

        using var doc = JsonDocument.Parse(report.ToJson());
        var root = doc.RootElement;

        Assert.Equal("minos-report", root.GetProperty("kind").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("fixture", root.GetProperty("header").GetProperty("datasetName").GetString());
        var noul = root.GetProperty("noul");
        Assert.Equal(100, noul.GetProperty("sweep").GetArrayLength());
        Assert.Equal(report.Noul!.ExpectedCalibrationError!.Value, noul.GetProperty("ece").GetDouble());
        Assert.Equal(report.Overall.ExpectedCalibrationErrorEqualWidth!.Value, root.GetProperty("overall").GetProperty("eceEqualWidth").GetDouble());
        Assert.Equal(report.Noul.Bins.Count, noul.GetProperty("bins").GetArrayLength());
        Assert.Equal(report.Latency.P95Ms, root.GetProperty("latency").GetProperty("p95Ms").GetDouble());
        Assert.Equal(2, root.GetProperty("errors").GetProperty("byKind").GetArrayLength());
    }

    [Fact]
    public void Json_MatchesTheSnapshot() => TextSnapshot.VerifyText(AllKindsReport().ToJson(), "json");

    [Fact]
    public void Json_UsesLineFeedsOnly() => Assert.DoesNotContain('\r', AllKindsReport().ToJson());

    [Fact]
    public void Json_KindsWithoutScoredQuestionsAreNull_AndEverySweepHas100Points()
    {
        using var noulless = JsonDocument.Parse(Report(Results(), Dataset()).ToJson());
        Assert.Equal(JsonValueKind.Null, noulless.RootElement.GetProperty("noul").ValueKind);

        using var all = JsonDocument.Parse(AllKindsReport().ToJson());
        foreach (var kind in new[] { "noul", "choice", "score", "overall" })
        {
            Assert.Equal(100, all.RootElement.GetProperty(kind).GetProperty("sweep").GetArrayLength());
        }
    }

    [Fact]
    public void Markdown_MatchesTheSnapshot() => TextSnapshot.VerifyText(AllKindsReport().ToMarkdown(), "md");

    [Fact]
    public void Markdown_SectionsInOrder()
    {
        var markdown = AllKindsReport().ToMarkdown();

        string[] headings =
        [
            "# Calibration report: ",
            "Dataset: ",
            "## Errors",
            "## Summary",
            "## Reliability",
            "## Threshold sweep",
            "## Latency",
            "## Cost",
            "## How to read this",
        ];
        var at = -1;
        foreach (var heading in headings)
        {
            var next = markdown.IndexOf(heading, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"'{heading}' is missing or out of order.");
            at = next;
        }
    }

    [Fact]
    public void Markdown_TitleHasProviderModelAndFinishedDate()
    {
        var first = AllKindsReport().ToMarkdown().Split('\n')[0].TrimEnd('\r');

        Assert.Equal("# Calibration report: fake jev-1.14, 2026-10-17", first);
    }

    [Fact]
    public void Markdown_ListsTheModelsThatServedTheResponses()
    {
        var markdown = AllKindsReport().ToMarkdown();

        Assert.Contains("Responses served by: no model named (10), jev-1.14-20261014 (4)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyAndAllFailed_RenderWithoutNaN()
    {
        var empty = Report(Results(), Dataset());
        var failed = Report(
            Results(Failed(1, "Timeout"), Failed(2)),
            Dataset(NoulLine(Id(1), true), NoulLine(Id(2), false)));

        foreach (var report in new[] { empty, failed })
        {
            var json = report.ToJson();
            var markdown = report.ToMarkdown();
            foreach (var text in new[] { json, markdown })
            {
                Assert.DoesNotContain("NaN", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Infinity", text, StringComparison.Ordinal);
            }

            Assert.Contains("n/a", markdown, StringComparison.Ordinal);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("overall").GetProperty("ece").ValueKind);
            Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("latency").GetProperty("medianMs").ValueKind);
        }
    }

    [Fact]
    public void Markdown_UsesTheInvariantCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
            var report = AllKindsReport();
            Assert.Equal(report.ToMarkdown(), AllKindsReport().ToMarkdown());
            Assert.Contains("0.7143", report.ToMarkdown(), StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\d,\d{4}", report.ToJson());
            Assert.DoesNotMatch(@"\d,\d{4}", report.ToMarkdown());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
