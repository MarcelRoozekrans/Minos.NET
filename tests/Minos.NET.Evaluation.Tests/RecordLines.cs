using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Minos.Recording;

namespace Minos.Evaluation.Tests;

// Builds record file lines exactly as docs/recording.md's "The JSONL format" shows them, and exports them through the
// real reader, so the export tests start from the bytes a recorder writes.
internal static class RecordLines
{
    // The three questions of the export design's example: a Noul, a Choice and a Score.
    public const string Questions =
        """{"urgent":{"type":"noul","instructions":"Does this convey urgency?"},"queue":{"type":"choice","instructions":"Which queue should handle this?","criteria":{"billing":"Payments, invoices and refunds","technical":"Bugs, outages and integrations"}},"mood":{"type":"score","instructions":"How upset is the customer?","criteria":["calm","annoyed","angry"]}}""";

    // The answers a successful decision on the default questions recorded.
    private const string DefaultAnswers =
        """[{"key":"urgent","kind":"noul","probability":0.9,"confidence":0.8},{"key":"queue","kind":"choice","chosenIndex":1,"confidence":0.8,"probabilities":[0.2,0.8]},{"key":"mood","kind":"score","chosenIndex":0,"expected":0.4,"confidence":0.7,"probabilities":[0.7,0.2,0.1]}]""";

    public static string Record(
        string id, string questions = Questions, string state = "\"The app crashes when I pay.\"", int images = 0, string? errorKind = null, bool cached = false,
        string[]? refusedKeys = null)
    {
        var line = new StringBuilder();
        line.Append("""{"kind":"record","version":1,"decisionId":""").Append(Quote(id))
            .Append(""","started":"2026-10-22T09:30:00+00:00","latencyMs":212.5,"provider":"typesafe","endpoint":"https://api.example.com/","defaultModel":"jev-latest","cached":""")
            .Append(cached ? "true" : "false")
            .Append(""","questions":""").Append(questions)
            .Append(""","state":""").Append(state)
            .Append(""","imageCount":""").Append(images.ToString(CultureInfo.InvariantCulture));
        if (images > 0)
        {
            line.Append(""","imageHashes":[""");
            for (var i = 0; i < images; i++)
            {
                line.Append(i == 0 ? "" : ",").Append(Quote("sha256:" + i.ToString(CultureInfo.InvariantCulture)));
            }

            line.Append(']');
        }

        if (errorKind is not null)
        {
            line.Append(""","errorKind":""").Append(Quote(errorKind));
            if (refusedKeys is not null)
            {
                line.Append(""","refusedKeys":[""").Append(string.Join(',', refusedKeys.Select(Quote))).Append(']');
            }
        }
        else if (string.Equals(questions, Questions, StringComparison.Ordinal))
        {
            line.Append(""","answers":""").Append(DefaultAnswers);
        }

        return line.Append('}').ToString();
    }

    public static string Confirmed(string id, string at, string source, string confirmedJson)
        => $$"""{"kind":"outcome","version":1,"decisionId":{{Quote(id)}},"type":"confirmed","source":{{Quote(source)}},"at":{{Quote(at)}},"confirmed":{{confirmedJson}}}""";

    public static string Signal(string id, string at, string source, string name, double? value = null, string? note = null)
    {
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $$"""{"kind":"outcome","version":1,"decisionId":{{Quote(id)}},"type":"signal","source":{{Quote(source)}},"at":{{Quote(at)}},"name":{{Quote(name)}}""");
        if (value is { } number)
        {
            line.Append(""","value":""").Append(number.ToString("R", CultureInfo.InvariantCulture));
        }

        if (note is not null)
        {
            line.Append(""","note":""").Append(Quote(note));
        }

        return line.Append('}').ToString();
    }

    public static string Erasure(string id) => $$"""{"kind":"erasure","version":1,"decisionId":{{Quote(id)}}}""";

    public static async Task<List<RecordedDecision>> ReadAsync(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-export-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            await File.WriteAllTextAsync(path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
            var decisions = new List<RecordedDecision>();
            await foreach (var decision in DecisionRecordFile.ReadAsync(path, CancellationToken.None))
            {
                decisions.Add(decision);
            }

            return decisions;
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static async Task<(DatasetExportSummary Summary, string Dataset, string Signals)> ExportAsync(params string[] lines)
    {
        var decisions = await ReadAsync(lines);
        using var dataset = new MemoryStream();
        using var signals = new MemoryStream();
        var summary = await DecisionDatasetExport.WriteAsync(Yield(decisions), dataset, signals, CancellationToken.None);
        return (summary, Encoding.UTF8.GetString(dataset.ToArray()), Encoding.UTF8.GetString(signals.ToArray()));
    }

    // Yields the decisions without looking at the token, so the export's own cancellation check is what throws.
    public static async IAsyncEnumerable<RecordedDecision> Yield(
        IEnumerable<RecordedDecision> decisions, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        foreach (var decision in decisions)
        {
            await Task.Yield();
            yield return decision;
        }
    }

    private static string Quote(string text) => "\"" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
}
