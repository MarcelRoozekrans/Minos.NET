using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Minos.Evaluation;

// The report as versioned JSON, with every number at full precision and every unknown number as null.
internal static class ReportJsonWriter
{
    internal const string Kind = "minos-report";

    internal const int Version = 1;

    private static readonly JsonWriterOptions Options = new() { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static string Write(CalibrationReport report)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = Writer(buffer))
        {
            w.WriteStartObject();
            w.WriteString("kind", Kind);
            w.WriteNumber("version", Version);
            WriteHeader(w, report.Header);
            w.WriteNumber("datasetItemCount", report.DatasetItemCount);
            WriteKind(w, "noul", report.Noul);
            WriteKind(w, "choice", report.Choice);
            WriteKind(w, "score", report.Score);
            WriteKind(w, "overall", report.Overall);
            w.WriteNumber("unscoredQuestions", report.UnscoredQuestions);
            if (report.VerbalizedAnswers > 0)
            {
                // Only when there are some, so a report without them is the same as before sources were kept.
                w.WriteNumber("verbalizedAnswers", report.VerbalizedAnswers);
            }

            WriteLatency(w, report.Latency);
            WriteCost(w, report.Cost);
            WriteErrors(w, report.Errors);
            WriteModels(w, report.ResponseModels);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // The writer the reports share: indented with line feeds, and no escaping of text that JSON allows as it is.
    internal static Utf8JsonWriter Writer(ArrayBufferWriter<byte> buffer) => new(buffer, Options);

    private static void WriteHeader(Utf8JsonWriter w, RunHeader h)
    {
        w.WriteStartObject("header");
        w.WriteString("datasetName", h.DatasetName);
        w.WriteNumber("itemCount", h.ItemCount);
        w.WriteString("providerName", h.ProviderName);
        w.WriteString("endpoint", h.Endpoint?.OriginalString);
        w.WriteString("defaultModel", h.DefaultModel);
        w.WriteString("started", h.Started.ToUniversalTime());
        w.WriteString("finished", h.Finished.ToUniversalTime());
        w.WriteNumber("parallelism", h.Parallelism);
        w.WriteBoolean("partial", h.Partial);
        w.WriteEndObject();
    }

    internal static void WriteKind(Utf8JsonWriter w, string name, KindMetrics? m)
    {
        if (m is null)
        {
            w.WriteNull(name);
            return;
        }

        w.WriteStartObject(name);
        w.WriteNumber("scored", m.Scored);
        w.WriteNumber("answered", m.Answered);
        WriteNumber(w, "accuracy", m.Accuracy);
        WriteNumber(w, "baseRate", m.BaseRate);
        WriteNumber(w, "ece", m.ExpectedCalibrationError);
        WriteNumber(w, "eceEqualWidth", m.ExpectedCalibrationErrorEqualWidth);
        w.WriteStartArray("bins");
        foreach (var bin in m.Bins)
        {
            w.WriteStartObject();
            w.WriteNumber("count", bin.Count);
            w.WriteNumber("meanConfidence", bin.MeanConfidence);
            w.WriteNumber("accuracy", bin.Accuracy);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteStartArray("sweep");
        foreach (var point in m.Sweep)
        {
            w.WriteStartObject();
            w.WriteNumber("threshold", point.Threshold);
            w.WriteNumber("coverage", point.Coverage);
            WriteNumber(w, "accuracy", point.Accuracy);
            w.WriteNumber("covered", point.Covered);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    internal static void WriteLatency(Utf8JsonWriter w, LatencySummary latency)
    {
        w.WriteStartObject("latency");
        w.WriteNumber("requests", latency.Requests);
        WriteNumber(w, "medianMs", latency.MedianMs);
        WriteNumber(w, "p95Ms", latency.P95Ms);
        w.WriteEndObject();
    }

    internal static void WriteErrors(Utf8JsonWriter w, ErrorSummary errors)
    {
        w.WriteStartObject("errors");
        w.WriteNumber("count", errors.Count);
        WriteCounts(w, "byKind", "kind", errors.ByKind);
        w.WriteEndObject();
    }

    // A list of named counts, as objects of the name and its count.
    internal static void WriteCounts(Utf8JsonWriter w, string name, string nameMember, IReadOnlyList<KeyValuePair<string, int>> counts)
    {
        w.WriteStartArray(name);
        foreach (var (key, count) in counts)
        {
            w.WriteStartObject();
            w.WriteString(nameMember, key);
            w.WriteNumber("count", count);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    internal static void WriteModels(Utf8JsonWriter w, IReadOnlyList<ModelCount> models)
    {
        w.WriteStartArray("responseModels");
        foreach (var model in models)
        {
            w.WriteStartObject();
            w.WriteString("model", model.Model);
            w.WriteNumber("count", model.Count);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    internal static void WriteCost(Utf8JsonWriter w, CostSummary c)
    {
        w.WriteStartObject("cost");
        WriteNumber(w, "perThousandDecisions", c.PerThousandDecisions);
        w.WriteNumber("reported", c.Reported);
        w.WriteNumber("priced", c.Priced);
        w.WriteNumber("unknown", c.Unknown);
        w.WriteNumber("cached", c.Cached);
        w.WriteStartArray("pricesUsed");
        foreach (var price in c.PricesUsed)
        {
            w.WriteStartObject();
            w.WriteString("model", price.Model);
            w.WriteNumber("inputPerMillion", price.InputPerMillion);
            w.WriteNumber("outputPerMillion", price.OutputPerMillion);
            w.WriteString("date", price.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("source", price.Source);
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }

    internal static void WriteNumber(Utf8JsonWriter w, string name, double? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteNull(name);
        }
    }
}
