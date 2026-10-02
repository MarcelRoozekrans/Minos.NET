using System.Text.RegularExpressions;

namespace ZeroAlloc.Jev.Docs.Tests;

/// <summary>Reads the Markdown tables of a guide page, so that a test can compare a table with the code it describes.</summary>
internal static partial class PageTables
{
    /// <summary>The text of a page under <c>docs/</c>.</summary>
    public static string Text(string page) => File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", page));

    /// <summary>
    /// The body rows of the first table after the heading whose text is <paramref name="heading"/>, each as its trimmed
    /// cells. The header and separator rows are skipped.
    /// </summary>
    public static string[][] Rows(string page, string heading)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "docs", page));
        var start = Array.FindIndex(lines, line => line.StartsWith('#') && string.Equals(line.TrimStart('#').Trim(), heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{page} has a heading '{heading}'.");

        var rows = new List<string[]>();
        foreach (var line in lines[(start + 1)..])
        {
            if (line.StartsWith('|'))
            {
                rows.Add(Cells(line));
            }
            else if (rows.Count > 0 || line.StartsWith('#'))
            {
                break;
            }
        }

        Assert.True(rows.Count > 2, $"The table under '{heading}' in {page} has rows.");
        return [.. rows.Skip(2)];
    }

    /// <summary>The first inline code span in a cell, without its backticks.</summary>
    public static string Code(string cell)
    {
        var match = CodeSpan().Match(cell);
        Assert.True(match.Success, $"'{cell}' has a code span.");
        return match.Groups["code"].Value;
    }

    /// <summary>Every inline code span in a cell, in order, without their backticks.</summary>
    public static string[] AllCode(string cell)
    {
        var spans = new List<string>();
        foreach (Match match in CodeSpan().Matches(cell))
        {
            spans.Add(match.Groups["code"].Value);
        }

        return [.. spans];
    }

    private static string[] Cells(string line)
        => Array.ConvertAll(line.Trim().Trim('|').Split('|'), cell => cell.Trim());

    [GeneratedRegex("`(?<code>[^`]+)`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeSpan();
}
