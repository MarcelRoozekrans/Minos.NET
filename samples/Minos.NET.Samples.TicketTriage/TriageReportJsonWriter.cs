using System.Text;
using System.Text.Json;
using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>Writes the triage report as indented JSON with <c>\n</c> line endings. Ratios are rounded to 4 decimals; no clock is written.</summary>
internal static class TriageReportJsonWriter
{
    public static string Write(TriageReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            WriteRun(w, report.Run);
            Kinds(w, "split", report.Split, WriteSplit);
            Kinds(w, "accuracy", report.Accuracy, WriteAccuracy);
            Kinds(w, "escalated", report.Escalated, WriteEscalated);
            Kinds(w, "signal", report.Signal, WriteSignal);
            WriteLatency(w, report.Latency);
            WriteCost(w, report.Cost);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void Kinds<T>(Utf8JsonWriter w, string name, PerKind<T> kinds, Action<Utf8JsonWriter, T> write)
    {
        w.WriteStartObject(name);
        w.WritePropertyName("choice");
        write(w, kinds.Choice);
        w.WritePropertyName("noul");
        write(w, kinds.Noul);
        w.WritePropertyName("all");
        write(w, kinds.All);
        w.WriteEndObject();
    }

    private static void WriteRun(Utf8JsonWriter w, RunSection run)
    {
        w.WriteStartObject("run");
        w.WriteNumber("itemsRead", run.ItemsRead);
        w.WriteNumber("decided", run.Decided);
        Counts(w, "failuresByError", run.FailuresByError);
        Counts(w, "servedModels", run.ServedModels);
        w.WriteEndObject();
    }

    private static void Counts(Utf8JsonWriter w, string name, IReadOnlyDictionary<string, int> counts)
    {
        w.WriteStartObject(name);
        foreach (var key in counts.Keys.Order(StringComparer.Ordinal))
        {
            w.WriteNumber(key, counts[key]);
        }

        w.WriteEndObject();
    }

    private static void WriteSplit(Utf8JsonWriter w, SplitCounts s)
    {
        w.WriteStartObject();
        w.WriteNumber("confident", s.Confident);
        w.WriteNumber("escalated", s.Escalated);
        w.WriteNumber("noTimeLeft", s.NoTimeLeft);
        w.WriteNumber("targetFailed", s.TargetFailed);
        w.WriteNumber("ticketsWithTargetCall", s.TicketsWithTargetCall);
        w.WriteEndObject();
    }

    private static void WriteAccuracy(Utf8JsonWriter w, AccuracySection a)
    {
        w.WriteStartObject();
        WriteHits(w, "localAlone", a.LocalAlone);
        WriteHits(w, "final", a.Final);
        WriteHits(w, "target", a.Target);
        w.WriteEndObject();
    }

    private static void WriteHits(Utf8JsonWriter w, string name, Hits hits)
    {
        w.WriteStartObject(name);
        w.WriteNumber("correct", hits.Correct);
        w.WriteNumber("total", hits.Total);
        Ratio(w, "rate", hits.Rate);
        w.WriteEndObject();
    }

    private static void WriteEscalated(Utf8JsonWriter w, EscalatedTable e)
    {
        w.WriteStartObject();
        w.WriteNumber("fixed", e.Fixed);
        w.WriteNumber("broken", e.Broken);
        w.WriteNumber("bothRight", e.BothRight);
        w.WriteNumber("bothWrong", e.BothWrong);
        w.WriteEndObject();
    }

    private static void WriteSignal(Utf8JsonWriter w, SignalSection s)
    {
        w.WriteStartObject();
        w.WriteNumber("wrongCaught", s.WrongCaught);
        w.WriteNumber("wrongMissed", s.WrongMissed);
        w.WriteNumber("belowThreshold", s.BelowThreshold);
        w.WriteNumber("belowThresholdLocalRight", s.BelowThresholdLocalRight);
        w.WriteEndObject();
    }

    private static void WriteLatency(Utf8JsonWriter w, LatencySection l)
    {
        w.WriteStartObject("latency");
        Time(w, "withTarget", l.WithTarget);
        Time(w, "withoutTarget", l.WithoutTarget);
        Time(w, "local", l.Local);
        Time(w, "target", l.Target);
        w.WriteEndObject();
    }

    private static void Time(Utf8JsonWriter w, string name, TimeSummary t)
    {
        w.WriteStartObject(name);
        w.WriteNumber("count", t.Count);
        Millis(w, "medianMs", t.Median);
        Millis(w, "p95Ms", t.P95);
        w.WriteEndObject();
    }

    private static void WriteCost(Utf8JsonWriter w, CostSection c)
    {
        w.WriteStartObject("cost");
        // The G6 round trip rounds the money to 6 significant digits, as the Markdown shows it.
        if (c.PerThousandTickets is { } cost)
        {
            w.WriteNumber("perThousandTickets", double.Parse(cost.ToString("G6", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            w.WriteNull("perThousandTickets");
        }

        w.WriteNumber("reported", c.Reported);
        w.WriteNumber("priced", c.Priced);
        w.WriteNumber("unknown", c.Unknown);
        w.WriteStartArray("pricesUsed");
        foreach (var p in c.PricesUsed)
        {
            w.WriteStartObject();
            w.WriteString("model", p.Model);
            w.WriteNumber("inputPerMillion", p.InputPerMillion);
            w.WriteNumber("outputPerMillion", p.OutputPerMillion);
            w.WriteString("date", p.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("source", p.Source);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void Ratio(Utf8JsonWriter w, string name, double? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, Math.Round(v, 4));
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static void Millis(Utf8JsonWriter w, string name, double? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, Math.Round(v, 1));
        }
        else
        {
            w.WriteNull(name);
        }
    }
}
