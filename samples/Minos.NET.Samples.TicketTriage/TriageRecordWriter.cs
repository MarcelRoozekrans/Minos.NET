using System.Text;
using System.Text.Json;

namespace Minos.Samples.TicketTriage;

/// <summary>Writes a <see cref="TriageRecord"/> as one JSON line. Null members are left out; the numbers are invariant.</summary>
internal static class TriageRecordWriter
{
    public static string Line(TriageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteString("id", record.Id);
            w.WriteNumber("totalMs", record.TotalMs);
            if (record.Error is not null)
            {
                w.WriteString("error", record.Error);
            }

            w.WriteStartArray("questions");
            foreach (var q in record.Questions)
            {
                w.WriteStartObject();
                w.WriteString("key", q.Key);
                WriteAnswer(w, "local", q.Local);
                w.WriteString("answeredBy", q.AnsweredBy);
                w.WriteString("reason", q.Reason);
                w.WriteNumber("primaryCertainty", q.PrimaryCertainty);
                if (q.TargetCertainty is { } target)
                {
                    w.WriteNumber("targetCertainty", target);
                }

                WriteAnswer(w, "final", q.Final);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("calls");
            foreach (var c in record.Calls)
            {
                w.WriteStartObject();
                w.WriteString("client", c.Client);
                if (c.Model is not null)
                {
                    w.WriteString("model", c.Model);
                }

                if (c.InputTokens is { } input)
                {
                    w.WriteNumber("inputTokens", input);
                }

                if (c.OutputTokens is { } output)
                {
                    w.WriteNumber("outputTokens", output);
                }

                if (c.Cost is { } cost)
                {
                    w.WriteNumber("cost", cost);
                }

                w.WriteString("status", c.Status);
                if (c.LatencyMs is { } latency)
                {
                    w.WriteNumber("latencyMs", latency);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static void WriteAnswer(Utf8JsonWriter w, string name, TriageAnswer answer)
    {
        w.WriteStartObject(name);
        w.WriteString("kind", answer.Kind);
        if (answer.Probability is { } p)
        {
            w.WriteNumber("p", p);
        }

        if (answer.ChoiceKey is not null)
        {
            w.WriteString("key", answer.ChoiceKey);
        }

        if (answer.Confidence is { } confidence)
        {
            w.WriteNumber("confidence", confidence);
        }

        w.WriteEndObject();
    }
}
