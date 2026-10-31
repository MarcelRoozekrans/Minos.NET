using System.Text;
using static Minos.Evaluation.ReportMarkdownWriter;

namespace Minos.Evaluation;

// The shadow report as Markdown, in the calibration report's style and with its writers for the shared sections: ratios
// at 4 decimals, an unknown number as n/a, the invariant culture throughout.
internal static class ShadowReportMarkdownWriter
{
    // A confusion table up to this many labels is printed in full; a larger one as its most frequent disagreements.
    private const int FullTableLabels = 12;

    private const int TopDisagreements = 20;

    private const string SmallMark = " (small: n < 30)";

    internal static string Write(ShadowReport report)
    {
        var md = new StringBuilder();
        md.Append("# Shadow report\n\n")
            .Append("Decisions read: ").Append(Count(report.DecisionsRead)).Append(". Shadowed: ").Append(Count(report.DecisionsShadowed))
            .Append(". Only the shadowed decisions are compared, and each side below covers those.\n\n");

        WriteSide(md, "Primary", report.Primary, report.DecisionsShadowed);
        WriteSide(md, "Candidate", report.Candidate, report.DecisionsShadowed);
        WriteAgreement(md, report.Questions);
        WriteConfusion(md, report.Questions);
        WriteSignals(md, report.Questions);
        WriteCalibration(md, report);
        WriteHowToRead(md);
        return md.ToString();
    }

    private static void WriteSide(StringBuilder md, string name, ShadowSide side, int decisions)
    {
        md.Append("## ").Append(name).Append("\n\n")
            .Append("Responses served by: ").Append(Models(side.Models)).Append("\n\n")
            .Append("Errors: ");
        WriteErrorsText(md, side.Errors, decisions);
        md.Append("\n\nRefused questions: ").Append(Count(side.Refusals)).Append(".\n\n");
        if (side.VerbalizedAnswers > 0)
        {
            md.Append("Verbalized probabilities: ").Append(Count(side.VerbalizedAnswers))
                .Append(side.VerbalizedAnswers == 1 ? " calibrated answer has" : " calibrated answers have")
                .Append(" probabilities the model stated in its text, not a measured distribution. The calibration metrics include them.\n\n");
        }

        WriteLatencyText(md.Append("Latency: "), side.Latency).Append("\n\n");
        md.Append("Cost: ");
        WriteCostText(md, side.Cost);
    }

    private static void WriteAgreement(StringBuilder md, IReadOnlyList<ShadowQuestion> questions)
    {
        md.Append("## Agreement\n\n");
        if (questions.Count == 0)
        {
            md.Append("No shadowed decision.\n\n");
            return;
        }

        md.Append("| Question | Kind | Compared | Agreement | Mean level difference | Primary only | Candidate only |\n")
            .Append("| --- | --- | ---: | ---: | ---: | ---: | ---: |\n");
        foreach (var q in questions)
        {
            md.Append("| ").Append(Cell(q.Key)).Append(" | ").Append(KindName(q.Kind)).Append(" | ").Append(Count(q.Compared)).Append(q.IsSmall ? SmallMark : string.Empty)
                .Append(" | ").Append(Ratio(q.Agreement)).Append(" | ").Append(Ratio(q.MeanLevelDifference))
                .Append(" | ").Append(Count(q.PrimaryOnly)).Append(" | ").Append(Count(q.CandidateOnly)).Append(" |\n");
        }

        md.Append('\n');
    }

    private static void WriteConfusion(StringBuilder md, IReadOnlyList<ShadowQuestion> questions)
    {
        md.Append("## Confusion\n\nThe primary's answers are the rows, the candidate's the columns.\n\n");
        var any = false;
        foreach (var q in questions)
        {
            if (q.Kind == QuestionKind.Noul || q.Compared == 0)
            {
                continue;
            }

            any = true;
            md.Append("### ").Append(Line(q.Key)).Append(", ").Append(KindName(q.Kind)).Append(q.IsSmall ? SmallMark : string.Empty).Append("\n\n");
            if (q.Labels.Count <= FullTableLabels)
            {
                WriteFullTable(md, q);
            }
            else
            {
                WriteTopDisagreements(md, q);
            }
        }

        if (!any)
        {
            md.Append("No Choice or Score question was compared.\n\n");
        }
    }

    private static void WriteFullTable(StringBuilder md, ShadowQuestion q)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < q.Labels.Count; i++)
        {
            index[q.Labels[i]] = i;
        }

        var counts = new int[q.Labels.Count, q.Labels.Count];
        foreach (var cell in q.Confusion)
        {
            counts[index[cell.Primary], index[cell.Candidate]] = cell.Count;
        }

        md.Append("| Primary \\ Candidate |");
        foreach (var label in q.Labels)
        {
            md.Append(' ').Append(Cell(label)).Append(" |");
        }

        md.Append("\n| --- |");
        foreach (var _ in q.Labels)
        {
            md.Append(" ---: |");
        }

        md.Append('\n');
        for (var row = 0; row < q.Labels.Count; row++)
        {
            md.Append("| ").Append(Cell(q.Labels[row])).Append(" |");
            for (var column = 0; column < q.Labels.Count; column++)
            {
                md.Append(' ').Append(Count(counts[row, column])).Append(" |");
            }

            md.Append('\n');
        }

        md.Append('\n');
    }

    // The most frequent cells off the diagonal; ties by the primary's label, then the candidate's, ordinally.
    private static void WriteTopDisagreements(StringBuilder md, ShadowQuestion q)
    {
        var disagreements = q.Confusion.Where(static c => !string.Equals(c.Primary, c.Candidate, StringComparison.Ordinal)).ToList();
        disagreements.Sort(static (a, b) => a.Count != b.Count
            ? b.Count.CompareTo(a.Count)
            : string.CompareOrdinal(a.Primary, b.Primary) is var byPrimary and not 0 ? byPrimary : string.CompareOrdinal(a.Candidate, b.Candidate));
        var shown = Math.Min(TopDisagreements, disagreements.Count);
        md.Append("With ").Append(Count(q.Labels.Count)).Append(q.Kind == QuestionKind.Score ? " levels" : " options")
            .Append(", the table is too wide to print. ");
        if (shown == 0)
        {
            md.Append("The two sides never disagreed.\n\n");
            return;
        }

        md.Append("The ").Append(Count(shown)).Append(" most frequent disagreements, of ").Append(Count(disagreements.Count))
            .Append(", are below; the JSON has every cell.\n\n| Primary | Candidate | Count |\n| --- | --- | ---: |\n");
        foreach (var cell in disagreements.Take(shown))
        {
            md.Append("| ").Append(Cell(cell.Primary)).Append(" | ").Append(Cell(cell.Candidate)).Append(" | ").Append(Count(cell.Count)).Append(" |\n");
        }

        md.Append('\n');
    }

    private static void WriteSignals(StringBuilder md, IReadOnlyList<ShadowQuestion> questions)
    {
        md.Append("## Signals\n\nThe signal outcomes of the compared decisions, per question: agreed when the two sides gave that question the same answer, disagreed when their answers differed. Signals are never expected answers.\n\n");
        var rows = new StringBuilder();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var q in questions)
        {
            names.Clear();
            foreach (var (name, _) in q.SignalsWhenAgreed)
            {
                names.Add(name);
            }

            foreach (var (name, _) in q.SignalsWhenDisagreed)
            {
                names.Add(name);
            }

            foreach (var name in names)
            {
                rows.Append("| ").Append(Cell(q.Key)).Append(" | ").Append(Cell(name))
                    .Append(" | ").Append(Count(Find(q.SignalsWhenAgreed, name))).Append(" | ").Append(Count(Find(q.SignalsWhenDisagreed, name))).Append(" |\n");
            }
        }

        if (rows.Length == 0)
        {
            md.Append("No compared decision has a signal outcome.\n\n");
            return;
        }

        md.Append("| Question | Signal | Agreed | Disagreed |\n| --- | --- | ---: | ---: |\n").Append(rows).Append('\n');
    }

    private static int Find(IReadOnlyList<KeyValuePair<string, int>> counts, string name)
    {
        foreach (var (key, count) in counts)
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
            {
                return count;
            }
        }

        return 0;
    }

    private static void WriteCalibration(StringBuilder md, ShadowReport report)
    {
        var keys = report.Primary.CalibratedKeys;
        var decisions = report.Primary.CalibratedDecisions;
        md.Append("## Calibration\n\nBoth sides are measured on the same ").Append(Count(keys)).Append(keys == 1 ? " question" : " questions")
            .Append(" of ").Append(Count(decisions)).Append(decisions == 1 ? " decision" : " decisions")
            .Append(": those both sides answered and a confirmed outcome answered with an answer that fits the question. ")
            .Append(Count(report.CalibrationPrimaryOnlyKeys)).Append(report.CalibrationPrimaryOnlyKeys == 1 ? " confirmed key was" : " confirmed keys were")
            .Append(" answered by the primary only and ").Append(Count(report.CalibrationCandidateOnlyKeys))
            .Append(" by the candidate only; they are not calibrated.\n\n");
        WriteSideCalibration(md, "Primary", report.Primary);
        WriteSideCalibration(md, "Candidate", report.Candidate);
    }

    private static void WriteSideCalibration(StringBuilder md, string name, ShadowSide side)
    {
        var kinds = Kinds(side.Noul, side.Choice, side.Score, side.Overall);
        md.Append("### ").Append(name).Append("\n\n");
        WriteSummaryTable(md, kinds);
        WriteReliabilityTables(md, kinds, "#### Reliability, ");
    }

    private static void WriteHowToRead(StringBuilder md)
        => md.Append(
            """
            ## How to read this

            The candidate answered the same questions as the primary, for the same decisions, in the background. Agreement is the share of the decisions in which both answered a question that they answered the same way: a Noul answer counts as yes at a probability of 0.5 or more, a Choice answer by its option, a Score answer by its level. For a Score question the mean level difference shows how far apart they were. Agreement needs no confirmed outcome, so it says how alike the two are, not which one is right.

            Calibration says which one is right, and how well each one's confidence matches it. It counts only the questions both sides answered and a person confirmed, so the two are measured on the same items; the figures mean what they mean in a calibration report.

            A question with fewer than 30 comparisons is marked small: with so few, every figure moves a lot. Treat it as a first look, not a measurement.

            """.ReplaceLineEndings("\n"));

    private static string KindName(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "Noul",
        QuestionKind.Choice => "Choice",
        _ => "Score",
    };

    // Keeps a value on one line and in one table cell.
    private static string Cell(string value) => Line(value).Replace("|", "\\|", StringComparison.Ordinal);
}
