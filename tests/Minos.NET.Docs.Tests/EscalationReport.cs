using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The committed Banking77 reports that the Escalation pages quote, and the pages themselves.
internal static partial class EscalationReport
{
    private const string TriageFolder = "docs/reports/triage/banking77";

    private const string CalibrationFolder = "docs/reports/local/banking77";

    /// <summary>The triage run's report, triage-report.json.</summary>
    internal static JsonNode Triage { get; } = Load(TriageFolder, "triage-report.json");

    /// <summary>The local calibration run's report, report.json.</summary>
    internal static JsonNode Calibration { get; } = Load(CalibrationFolder, "report.json");

    /// <summary>The triage run's README.md, which records the run's configuration.</summary>
    internal static string TriageReadme { get; } = File.ReadAllText(Path.Combine(PublishedPages.Root, TriageFolder, "README.md"));

    /// <summary>The text of docs/escalation/&lt;name&gt;.md.</summary>
    internal static string PageText(string name) => File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", "escalation", name + ".md"));

    /// <summary>The text of docs/escalation/&lt;name&gt;.md with every run of whitespace collapsed to one space, so a re-wrap does not move a phrase.</summary>
    internal static string PageWords(string name) => string.Join(' ', PageText(name).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // A path such as split.all.escalated or questions[key=intent].agreement.
    internal static JsonNode Resolve(JsonNode root, string path)
    {
        var node = root;
        foreach (var part in path.Split('.'))
        {
            var select = Selector().Match(part);
            node = select.Success ? WithKey(node[select.Groups["name"].Value]!.AsArray(), select.Groups["key"].Value) : node[part];
            Assert.NotNull(node);
        }

        return node!;
    }

    private static JsonNode Load(string folder, string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, folder, file)))!;

    private static JsonNode WithKey(JsonArray items, string key)
    {
        foreach (var item in items)
        {
            if (string.Equals((string?)item!["key"], key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        throw new InvalidOperationException($"No item has the key {key}.");
    }

    [GeneratedRegex(@"^(?<name>\w+)\[key=(?<key>\w+)\]$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Selector();
}
