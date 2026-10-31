using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Minos.Evaluation;

// The shadow report as versioned JSON, laid out as the calibration report is: every number at full precision, every
// unknown number as null, and each side's metrics, latency, cost and errors written by the calibration report's own
// writers. Confusion tables are sparse: only their non-zero cells.
internal static class ShadowReportJsonWriter
{
    internal const string Kind = "minos-shadow-report";

    internal const int Version = 1;

    internal static string Write(ShadowReport report)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = ReportJsonWriter.Writer(buffer))
        {
            w.WriteStartObject();
            w.WriteString("kind", Kind);
            w.WriteNumber("version", Version);
            w.WriteNumber("decisionsRead", report.DecisionsRead);
            w.WriteNumber("decisionsShadowed", report.DecisionsShadowed);
            WriteSide(w, "primary", report.Primary);
            WriteSide(w, "candidate", report.Candidate);
            w.WriteStartArray("questions");
            foreach (var question in report.Questions)
            {
                WriteQuestion(w, question);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteSide(Utf8JsonWriter w, string name, ShadowSide side)
    {
        w.WriteStartObject(name);
        w.WriteNumber("calibratedDecisions", side.CalibratedDecisions);
        w.WriteNumber("calibratedKeys", side.CalibratedKeys);
        ReportJsonWriter.WriteKind(w, "noul", side.Noul);
        ReportJsonWriter.WriteKind(w, "choice", side.Choice);
        ReportJsonWriter.WriteKind(w, "score", side.Score);
        ReportJsonWriter.WriteKind(w, "overall", side.Overall);
        w.WriteNumber("verbalizedAnswers", side.VerbalizedAnswers);
        w.WriteNumber("refusals", side.Refusals);
        ReportJsonWriter.WriteLatency(w, side.Latency);
        ReportJsonWriter.WriteCost(w, side.Cost);
        ReportJsonWriter.WriteErrors(w, side.Errors);
        ReportJsonWriter.WriteModels(w, side.Models);
        w.WriteEndObject();
    }

    private static void WriteQuestion(Utf8JsonWriter w, ShadowQuestion question)
    {
        w.WriteStartObject();
        w.WriteString("key", question.Key);
        w.WriteString("kind", KindName(question.Kind));
        w.WriteStartArray("labels");
        foreach (var label in question.Labels)
        {
            w.WriteStringValue(label);
        }

        w.WriteEndArray();
        w.WriteNumber("compared", question.Compared);
        w.WriteNumber("primaryOnly", question.PrimaryOnly);
        w.WriteNumber("candidateOnly", question.CandidateOnly);
        w.WriteBoolean("small", question.IsSmall);
        ReportJsonWriter.WriteNumber(w, "agreement", question.Agreement);
        ReportJsonWriter.WriteNumber(w, "meanLevelDifference", question.MeanLevelDifference);
        w.WriteStartArray("confusion");
        foreach (var cell in question.Confusion)
        {
            w.WriteStartObject();
            w.WriteString("primary", cell.Primary);
            w.WriteString("candidate", cell.Candidate);
            w.WriteNumber("count", cell.Count);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        ReportJsonWriter.WriteCounts(w, "signalsWhenAgreed", "name", question.SignalsWhenAgreed);
        ReportJsonWriter.WriteCounts(w, "signalsWhenDisagreed", "name", question.SignalsWhenDisagreed);
        w.WriteEndObject();
    }

    // The kind's name as the record and dataset formats write it.
    private static string KindName(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "noul",
        QuestionKind.Choice => "choice",
        _ => "score",
    };
}
