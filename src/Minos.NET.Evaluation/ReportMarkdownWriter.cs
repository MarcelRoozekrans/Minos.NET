using System.Globalization;
using System.Text;

namespace Minos.Evaluation;

// The report as Markdown: ratios at 4 decimals, an unknown number as n/a, the invariant culture throughout.
internal static class ReportMarkdownWriter
{
    private static readonly int[] SweepPercents = [0, 30, 50, 60, 70, 80, 85, 90, 95, 99];

    internal static string Write(CalibrationReport report)
    {
        var md = new StringBuilder();
        var header = report.Header;
        var system = string.Join(' ', new[] { header.ProviderName, header.DefaultModel }.Where(s => !string.IsNullOrWhiteSpace(s)));
        md.Append("# Calibration report: ")
            .Append(Line(system.Length == 0 ? "unknown provider and model" : system))
            .Append(", ")
            .Append(header.Finished.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Append("\n\n");

        md.Append("Dataset: ").Append(Line(header.DatasetName)).Append(", ").Append(Count(header.ItemCount)).Append(header.ItemCount == 1 ? " item" : " items");
        if (report.UnscoredQuestions > 0)
        {
            md.Append(". ").Append(Count(report.UnscoredQuestions)).Append(report.UnscoredQuestions == 1 ? " question has" : " questions have")
                .Append(" no expected answer and ").Append(report.UnscoredQuestions == 1 ? "is" : "are").Append(" not scored");
        }

        md.Append(".\n\n");
        if (report.Partial)
        {
            md.Append("Partial run: ").Append(Count(header.ItemCount)).Append(" of ").Append(Count(report.DatasetItemCount))
                .Append(report.DatasetItemCount == 1 ? " item" : " items").Append(".\n\n");
        }

        if (report.VerbalizedAnswers > 0)
        {
            md.Append("Verbalized probabilities: ").Append(Count(report.VerbalizedAnswers))
                .Append(report.VerbalizedAnswers == 1 ? " answer has" : " answers have")
                .Append(" probabilities the model stated in its text, not a measured distribution. The calibration metrics include them.\n\n");
        }

        md.Append("Responses served by: ").Append(Models(report.ResponseModels)).Append("\n\n");

        WriteErrors(md, report.Errors, header.ItemCount);

        var kinds = Kinds(report);
        WriteSummary(md, kinds);
        WriteReliability(md, kinds);
        WriteSweep(md, kinds);
        WriteLatency(md, report.Latency);
        WriteCost(md, report.Cost);
        WriteHowToRead(md);
        return md.ToString();
    }

    private static List<(string Name, KindMetrics Metrics)> Kinds(CalibrationReport report)
    {
        var kinds = new List<(string, KindMetrics)>();
        if (report.Noul is { } noul)
        {
            kinds.Add(("Noul", noul));
        }

        if (report.Choice is { } choice)
        {
            kinds.Add(("Choice", choice));
        }

        if (report.Score is { } score)
        {
            kinds.Add(("Score", score));
        }

        // With one kind present, "all kinds" would repeat it.
        if (kinds.Count != 1)
        {
            kinds.Add((kinds.Count > 1 ? "All kinds" : "Overall", report.Overall));
        }

        return kinds;
    }

    private static string Models(IReadOnlyList<ModelCount> models)
        => models.Count == 0
            ? "none"
            : string.Join(", ", models.Select(m => Line(m.Model ?? "no model named") + " (" + Count(m.Count) + ")"));

    private static void WriteErrors(StringBuilder md, ErrorSummary errors, int items)
    {
        md.Append("## Errors\n\n");
        if (errors.Count == 0)
        {
            md.Append("No request failed.\n\n");
            return;
        }

        md.Append(Count(errors.Count)).Append(" of ").Append(Count(items)).Append(items == 1 ? " request" : " requests").Append(" failed: ")
            .Append(string.Join(", ", errors.ByKind.Select(e => Line(e.Key) + " " + Count(e.Value))))
            .Append(". A failed request is an abstention.\n\n");
    }

    private static void WriteSummary(StringBuilder md, List<(string Name, KindMetrics Metrics)> kinds)
    {
        md.Append("## Summary\n\n| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |\n| --- | ---: | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var (name, m) in kinds)
        {
            md.Append("| ").Append(name).Append(" | ").Append(Count(m.Scored)).Append(" | ").Append(Count(m.Answered))
                .Append(" | ").Append(Ratio(m.Accuracy)).Append(" | ").Append(Ratio(m.BaseRate))
                .Append(" | ").Append(Ratio(m.ExpectedCalibrationError)).Append(" | ").Append(Ratio(m.ExpectedCalibrationErrorEqualWidth)).Append(" |\n");
        }

        md.Append('\n');
    }

    private static void WriteReliability(StringBuilder md, List<(string Name, KindMetrics Metrics)> kinds)
    {
        md.Append("## Reliability\n\n");
        foreach (var (name, m) in kinds)
        {
            md.Append("### ").Append(name).Append("\n\n");
            if (m.Bins.Count == 0)
            {
                md.Append("No answers.\n\n");
                continue;
            }

            md.Append("| Bin | Answers | Mean confidence | Accuracy |\n| ---: | ---: | ---: | ---: |\n");
            for (var i = 0; i < m.Bins.Count; i++)
            {
                var bin = m.Bins[i];
                md.Append("| ").Append(Count(i + 1)).Append(" | ").Append(Count(bin.Count)).Append(" | ")
                    .Append(Ratio(bin.MeanConfidence)).Append(" | ").Append(Ratio(bin.Accuracy)).Append(" |\n");
            }

            md.Append('\n');
        }
    }

    private static void WriteSweep(StringBuilder md, List<(string Name, KindMetrics Metrics)> kinds)
    {
        md.Append("## Threshold sweep\n\nThe full sweep, from 0.00 to 0.99 in steps of 0.01, is in the JSON.\n\n");
        foreach (var (name, m) in kinds)
        {
            md.Append("### ").Append(name).Append("\n\n| Threshold | Coverage | Accuracy | Covered |\n| ---: | ---: | ---: | ---: |\n");
            foreach (var percent in SweepPercents)
            {
                if (percent >= m.Sweep.Count)
                {
                    continue;
                }

                var point = m.Sweep[percent];
                md.Append("| ").Append(point.Threshold.ToString("F2", CultureInfo.InvariantCulture)).Append(" | ").Append(Ratio(point.Coverage))
                    .Append(" | ").Append(Ratio(point.Accuracy)).Append(" | ").Append(Count(point.Covered)).Append(" |\n");
            }

            md.Append('\n');
        }
    }

    private static void WriteLatency(StringBuilder md, LatencySummary latency)
        => md.Append("## Latency\n\nMedian ").Append(Millis(latency.MedianMs)).Append(", p95 ").Append(Millis(latency.P95Ms))
            .Append(", over ").Append(Count(latency.Requests)).Append(latency.Requests == 1 ? " request" : " requests").Append(".\n\n");

    private static void WriteCost(StringBuilder md, CostSummary cost)
    {
        md.Append("## Cost\n\nPer 1,000 decisions: ").Append(Cost(cost.PerThousandDecisions)).Append(", in the currency of the prices and reported costs.\n\n")
            .Append("Cost came from the provider for ").Append(Count(cost.Reported)).Append(" items, from a price for ").Append(Count(cost.Priced))
            .Append(", and was unknown for ").Append(Count(cost.Unknown)).Append(". Cached responses left out: ").Append(Count(cost.Cached)).Append(".\n\n");
        if (cost.PricesUsed.Count == 0)
        {
            md.Append("Prices used: none.\n\n");
            return;
        }

        md.Append("Prices used, per million tokens:\n\n");
        foreach (var p in cost.PricesUsed)
        {
            md.Append("- ").Append(Line(p.Model)).Append(": ").Append(Number(p.InputPerMillion)).Append(" input, ").Append(Number(p.OutputPerMillion))
                .Append(" output, read ").Append(p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(" from ").Append(Line(p.Source)).Append('\n');
        }

        md.Append('\n');
    }

    private static void WriteHowToRead(StringBuilder md)
        => md.Append(
            """
            ## How to read this

            Every question with an expected answer is scored against it. The number a threshold gates on is the confidence of the answer: for a Noul question it is the larger of p, the model's probability that the answer is yes, and 1 - p, so it is never below 0.5; for a Choice or Score question it is the confidence the model reports, which can be lower. Use the sweep of each kind, not the one over all kinds, to choose a threshold.

            Answers are sorted by confidence and split into up to ten bins of equal size, give or take one answer. In a well-calibrated model the accuracy of each bin is close to its mean confidence. ECE, the expected calibration error, is the gap between the two averaged over the bins, weighted by how many answers each holds: 0 is perfect, and larger is worse. The equal-width column does the same over ten bins of equal confidence width, which can leave bins empty or crowded.

            The base rate is what you would score by always giving the most common expected answer for each question. It is worked out per question, over the answered questions only, from the same answers it is compared with, so it is high when a question has few answers: a question answered once always matches its base rate. An accuracy that does not beat it says little.

            A failed request is an abstention. Accuracy, base rate and calibration use the answers that came back; the sweep's coverage is a share of all scored questions, so a failed request counts as not covered. An InvalidResponse request was served and billed, so its cost is in the cost and its model in the responses served, but like every failed request it is left out of latency. Responses served from a cache are left out of latency and cost.

            With few answers every figure moves a lot: a bin of three answers can only show an accuracy of 0, a third, two thirds or 1. Treat results from a small sample as a first look, not a measurement.

            """.ReplaceLineEndings("\n"));

    private static string Ratio(double? value) => value is { } v ? v.ToString("F4", CultureInfo.InvariantCulture) : "n/a";

    private static string Cost(double? value) => value is { } v ? v.ToString("G6", CultureInfo.InvariantCulture) : "n/a";

    private static string Millis(double? value) => value is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) + " ms" : "n/a";

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    // Keeps a value on one line, so it cannot break the structure of the report.
    private static string Line(string value) => value.ReplaceLineEndings(" ");
}
