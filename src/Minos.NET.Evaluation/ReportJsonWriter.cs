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
        using (var w = new Utf8JsonWriter(buffer, Options))
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
            w.WriteStartObject("latency");
            w.WriteNumber("requests", report.Latency.Requests);
            WriteNumber(w, "medianMs", report.Latency.MedianMs);
            WriteNumber(w, "p95Ms", report.Latency.P95Ms);
            w.WriteEndObject();
            WriteCost(w, report.Cost);
            w.WriteStartObject("errors");
            w.WriteNumber("count", report.Errors.Count);
            w.WriteStartArray("byKind");
            foreach (var (kind, count) in report.Errors.ByKind)
            {
                w.WriteStartObject();
                w.WriteString("kind", kind);
                w.WriteNumber("count", count);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteStartArray("responseModels");
            foreach (var model in report.ResponseModels)
            {
                w.WriteStartObject();
                w.WriteString("model", model.Model);
                w.WriteNumber("count", model.Count);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

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

    private static void WriteKind(Utf8JsonWriter w, string name, KindMetrics? m)
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

    private static void WriteCost(Utf8JsonWriter w, CostSummary c)
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

    private static void WriteNumber(Utf8JsonWriter w, string name, double? value)
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
