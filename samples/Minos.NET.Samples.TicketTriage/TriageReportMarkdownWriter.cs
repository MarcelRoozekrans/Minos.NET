using System.Globalization;
using System.Text;
using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>
/// Writes the triage report as Markdown. It states what was measured and gives no verdict. Ratios are at 4 decimals, money at G6,
/// milliseconds at one decimal, an unknown number as n/a; every share is printed with its count and a base under 30 is marked small.
/// </summary>
internal static class TriageReportMarkdownWriter
{
    private const int SmallBelow = 30;

    private const string SmallMark = " (small: n < 30)";

    public static string Write(TriageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var md = new StringBuilder();
        md.Append("# Triage report\n\n");
        WriteRun(md, report.Run);
        WriteSplit(md, report);
        WriteAccuracy(md, report.Accuracy);
        WriteEscalated(md, report.Escalated);
        WriteSignal(md, report.Signal);
        WriteLatency(md, report.Latency);
        WriteCost(md, report.Cost);
        WriteHowToRead(md);
        return md.ToString();
    }

    private static void WriteRun(StringBuilder md, RunSection run)
    {
        md.Append("## Run\n\n")
            .Append("Items read: ").Append(N(run.ItemsRead)).Append(" of the dataset's ").Append(N(run.DatasetItems)).Append(". Decided: ").Append(Share(run.Decided, run.ItemsRead)).Append(".\n\n")
            .Append("Failed items: ").Append(Pairs(run.FailuresByError)).Append("\n\n")
            .Append("Calls by served model: ").Append(Pairs(run.ServedModels)).Append("\n\n");
    }

    private static void WriteSplit(StringBuilder md, TriageReport report)
    {
        md.Append("## Split\n\nThe questions by the route reason, and the tickets whose path had a target call that was not skipped.\n\n");
        foreach (var (name, s) in Kinds(report.Split))
        {
            var total = s.Confident + s.Escalated + s.NoTimeLeft + s.TargetFailed;
            md.Append("### ").Append(name).Append("\n\n")
                .Append("- Questions: ").Append(N(total)).Append(Small(total)).Append('\n')
                .Append("- Confident, kept on the local answer: ").Append(Share(s.Confident, total)).Append('\n')
                .Append("- Escalated, answered by the target: ").Append(Share(s.Escalated, total)).Append('\n')
                .Append("- No time left, kept on the local answer: ").Append(Share(s.NoTimeLeft, total)).Append('\n')
                .Append("- Target failed, kept on the local answer: ").Append(Share(s.TargetFailed, total)).Append('\n');
            md.Append("- Tickets with a target call: ").Append(
                string.Equals(name, "All", StringComparison.Ordinal) ? Share(s.TicketsWithTargetCall, report.Run.Decided) : N(s.TicketsWithTargetCall) + Small(s.TicketsWithTargetCall)).Append("\n\n");
        }
    }

    private static void WriteAccuracy(StringBuilder md, PerKind<AccuracySection> accuracy)
    {
        md.Append("## Accuracy\n\nRight answers against the dataset's expected answers.\n\n");
        foreach (var (name, a) in Kinds(accuracy))
        {
            md.Append("### ").Append(name).Append("\n\n")
                .Append("- Local answer alone: ").Append(HitsText(a.LocalAlone)).Append('\n')
                .Append("- Final answer: ").Append(HitsText(a.Final)).Append('\n')
                .Append("- Target, on the escalated questions: ").Append(HitsText(a.Target)).Append("\n\n");
        }
    }

    private static void WriteEscalated(StringBuilder md, PerKind<EscalatedTable> escalated)
    {
        md.Append("## Escalated questions\n\nThe questions the target answered, by whether the local and the final answer were right.\n\n");
        foreach (var (name, e) in Kinds(escalated))
        {
            var total = e.Fixed + e.Broken + e.BothRight + e.BothWrong;
            md.Append("### ").Append(name).Append("\n\n")
                .Append("- Escalated: ").Append(N(total)).Append(Small(total)).Append('\n')
                .Append("- Fixed, local wrong and final right: ").Append(Share(e.Fixed, total)).Append('\n')
                .Append("- Broken, local right and final wrong: ").Append(Share(e.Broken, total)).Append('\n')
                .Append("- Both right: ").Append(Share(e.BothRight, total)).Append('\n')
                .Append("- Both wrong: ").Append(Share(e.BothWrong, total)).Append("\n\n");
        }
    }

    private static void WriteSignal(StringBuilder md, PerKind<SignalSection> signal)
    {
        md.Append("## The signal\n\nWhether the below-threshold flag found the local answers that were wrong. Caught means below the threshold, missed means it met the threshold.\n\n");
        foreach (var (name, s) in Kinds(signal))
        {
            var wrong = s.WrongCaught + s.WrongMissed;
            md.Append("### ").Append(name).Append("\n\n")
                .Append("- Wrong local answers: ").Append(N(wrong)).Append(Small(wrong)).Append('\n')
                .Append("- Caught: ").Append(Share(s.WrongCaught, wrong)).Append('\n')
                .Append("- Missed: ").Append(Share(s.WrongMissed, wrong)).Append('\n')
                .Append("- Local answers below the threshold: ").Append(N(s.BelowThreshold)).Append(Small(s.BelowThreshold)).Append('\n')
                .Append("- Of those, the local answer was right: ").Append(Share(s.BelowThresholdLocalRight, s.BelowThreshold)).Append("\n\n");
        }
    }

    private static void WriteLatency(StringBuilder md, LatencySection latency)
    {
        md.Append("## Latency\n\nThe time per ticket, and per call. Percentiles are nearest-rank.\n\n")
            .Append("- Tickets with a target call: ").Append(TimeText(latency.WithTarget)).Append('\n')
            .Append("- Tickets without a target call: ").Append(TimeText(latency.WithoutTarget)).Append('\n')
            .Append("- Local calls: ").Append(TimeText(latency.Local)).Append('\n')
            .Append("- Target calls: ").Append(TimeText(latency.Target)).Append("\n\n");
    }

    private static void WriteCost(StringBuilder md, CostSection cost)
    {
        md.Append("## Cost\n\n")
            .Append("Per 1,000 tickets: ").Append(Money(cost.PerThousandTickets)).Append(", in the currency of the prices and reported costs.\n\n")
            .Append("Cost came from the provider for ").Append(N(cost.Reported)).Append(" tickets, from a price for ").Append(N(cost.Priced))
            .Append(", and was unknown for ").Append(N(cost.Unknown)).Append(". A ticket with an unknown cost is left out of the total.\n\n");
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

            Every ticket was answered locally first. The local answer is the baseline: the accuracy of the local answer alone is what the run would have scored without escalation. A question is escalated when its local answer is below the threshold set for it, and the target's answer then replaces the local one.

            Fixed counts the escalated questions whose local answer was wrong and whose final answer is right. Broken counts the ones whose local answer was right and whose final answer is wrong. Caught counts the wrong local answers that were below the threshold, missed the wrong local answers that met it.

            A question kept on the local answer because no time was left, or because the target failed, counts as the local answer; the target is not credited for it. Cost and latency are measured over the decided tickets, and a ticket with an unknown cost is left out of the cost total.

            Every share is printed with the count it is taken from. A count under 30 is marked small: with so few, every figure moves a lot, so treat it as a first look, not a measurement.

            """.ReplaceLineEndings("\n"));

    private static IEnumerable<(string Name, T Value)> Kinds<T>(PerKind<T> kinds)
    {
        yield return ("Choice", kinds.Choice);
        yield return ("Noul", kinds.Noul);
        yield return ("All", kinds.All);
    }

    private static string HitsText(Hits hits) => Share(hits.Correct, hits.Total);

    private static string TimeText(TimeSummary t)
        => N(t.Count) + Small(t.Count) + ", median " + Millis(t.Median) + ", p95 " + Millis(t.P95);

    private static string Pairs(IReadOnlyDictionary<string, int> counts)
    {
        if (counts.Count == 0)
        {
            return "none.";
        }

        return string.Join(", ", counts.Keys.Order(StringComparer.Ordinal).Select(k => Line(k) + " " + N(counts[k]))) + ".";
    }

    // "count of total (ratio)", the base marked when it is small.
    private static string Share(int count, int total)
        => N(count) + " of " + N(total) + " (" + (total == 0 ? "n/a" : ((double)count / total).ToString("F4", CultureInfo.InvariantCulture)) + ")" + Small(total);

    private static string Small(int count) => count < SmallBelow ? SmallMark : string.Empty;

    private static string Millis(double? value) => value is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) + " ms" : "n/a";

    private static string Money(double? value) => value is { } v ? v.ToString("G6", CultureInfo.InvariantCulture) : "n/a";

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Line(string value) => value.ReplaceLineEndings(" ");
}
