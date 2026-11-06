using System.Globalization;
using System.Text.Json;

namespace Minos.Samples.TicketTriage;

/// <summary>
/// Reads a records file written by <see cref="TriageRecordWriter"/>. A bad line throws an
/// <see cref="InvalidDataException"/> that names the line number and never the line's text.
/// </summary>
internal static class TriageRecordReader
{
    private static readonly string[] Reasons = [nameof(RouteReason.Confident), nameof(RouteReason.Escalated), nameof(RouteReason.NoTimeLeft), nameof(RouteReason.TargetFailed)];
    private static readonly string[] Statuses = [nameof(CallStatus.Answered), nameof(CallStatus.Failed), nameof(CallStatus.NotCalledNoTimeLeft)];

    public static IReadOnlyList<TriageRecord> Read(string path)
    {
        var records = new List<TriageRecord>();
        var number = 0;
        foreach (var line in File.ReadLines(path))
        {
            number++;
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                records.Add(Parse(doc.RootElement));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Malformed record at line {number}."), ex);
            }
        }

        return records;
    }

    private static TriageRecord Parse(JsonElement root)
    {
        if (root.GetProperty("v").GetInt32() != 1)
        {
            throw new InvalidDataException();
        }

        var questions = new List<TriageQuestion>();
        foreach (var q in root.GetProperty("questions").EnumerateArray())
        {
            questions.Add(new TriageQuestion(
                Required(q, "key"),
                ParseAnswer(q.GetProperty("local")),
                Required(q, "answeredBy"),
                OneOf(q.GetProperty("reason").GetString(), Reasons),
                q.GetProperty("primaryCertainty").GetDouble(),
                Double(q, "targetCertainty"),
                ParseAnswer(q.GetProperty("final"))));
        }

        var calls = new List<TriageCall>();
        foreach (var c in root.GetProperty("calls").EnumerateArray())
        {
            calls.Add(new TriageCall(
                Required(c, "client"),
                String(c, "model"),
                Int(c, "inputTokens"),
                Int(c, "outputTokens"),
                Double(c, "cost"),
                OneOf(c.GetProperty("status").GetString(), Statuses),
                Double(c, "latencyMs")));
        }

        return new TriageRecord(
            Required(root, "id"),
            root.GetProperty("totalMs").GetDouble(),
            String(root, "error"),
            questions,
            calls);
    }

    private static TriageAnswer ParseAnswer(JsonElement e)
    {
        var kind = e.GetProperty("kind").GetString();
        return kind switch
        {
            "noul" => new TriageAnswer(kind, e.GetProperty("p").GetDouble(), null, null),
            "choice" => new TriageAnswer(kind, null, Required(e, "key"), e.GetProperty("confidence").GetDouble()),
            _ => throw new InvalidDataException(),
        };
    }

    private static string OneOf(string? value, string[] allowed) =>
        value is not null && Array.IndexOf(allowed, value) >= 0 ? value : throw new InvalidDataException();

    private static string Required(JsonElement e, string name) =>
        e.GetProperty(name).GetString() ?? throw new InvalidDataException();

    private static string? String(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static double? Double(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetDouble() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetInt32() : null;
}
