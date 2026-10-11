using System.Text.RegularExpressions;
using Minos.SystemOne;

namespace Minos.Docs.Tests;

// Keeps the Provider differences table in step with the code: its columns are the presets and OpenAI, and its MIN ids
// are the provider rules. It reads no cell's wording, so a cell can be reworded without breaking it.
public sealed partial class ProviderDifferencesTests
{
    private const string Page = "client-and-errors.md";

    private const string Heading = "### Provider differences";

    [Fact]
    public void TheColumns_AreThePresets_ThenOpenAI()
    {
        var (header, _) = Table();

        Assert.Equal([.. Enum.GetNames<SystemOneProvider>(), "OpenAI"], header.Skip(1).Select(Unquote));
    }

    [Fact]
    public void EveryRow_HasACellForEachColumn()
    {
        var (header, rows) = Table();

        Assert.Equal(12, rows.Count);
        Assert.All(rows, row => Assert.Equal(header.Length, row.Length));
        Assert.All(rows, row => Assert.All(row, cell => Assert.False(string.IsNullOrWhiteSpace(cell))));
    }

    [Fact]
    public void EveryProviderRuleInTheTable_IsADiagnosticId()
    {
        var ids = DiagnosticIdSource.ProviderRules();
        var (_, rows) = Table();
        var used = rows.SelectMany(row => row).SelectMany(cell => MinId().Matches(cell).Select(match => match.Value)).ToList();

        Assert.NotEmpty(used);
        Assert.All(used, id => Assert.Contains(id, ids));
    }

    [Fact]
    public void EveryProviderRule_AppearsInTheSection()
    {
        var ids = DiagnosticIdSource.ProviderRules();
        var section = Section();

        Assert.Equal(["MIN201", "MIN202", "MIN203", "MIN204", "MIN205", "MIN206"], ids);
        Assert.All(ids, id => Assert.Contains(id, section, StringComparison.Ordinal));
    }

    // The lines from the heading to the next heading of the same or a higher level.
    private static List<string> SectionLines()
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", Page));
        var start = Array.FindIndex(lines, line => string.Equals(line.TrimEnd(), Heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{Page} has a heading '{Heading}'.");

        var section = new List<string>();
        foreach (var line in lines[(start + 1)..])
        {
            if (Level().Match(line) is { Success: true } heading && heading.Groups["hashes"].Length <= 3)
            {
                break;
            }

            section.Add(line);
        }

        return section;
    }

    private static string Section() => string.Join('\n', SectionLines());

    // The first table of the section: its header cells and its body rows.
    private static (string[] Header, List<string[]> Rows) Table()
    {
        var table = SectionLines().SkipWhile(line => !line.StartsWith('|')).TakeWhile(line => line.StartsWith('|')).ToList();
        Assert.True(table.Count > 2, "The section starts with a table.");
        return (Cells(table[0]), [.. table.Skip(2).Select(Cells)]);
    }

    private static string[] Cells(string line)
        => Array.ConvertAll(line.Trim().Trim('|').Split('|'), cell => cell.Trim());

    private static string Unquote(string cell) => cell.Trim('`');

    [GeneratedRegex(@"\bMIN2\d\d\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MinId();

    [GeneratedRegex(@"^(?<hashes>#{1,6})\s", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Level();
}
