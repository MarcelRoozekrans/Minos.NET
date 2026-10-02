using System.Text;
using System.Text.RegularExpressions;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed partial class LinkTests
{
    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var page in PublishedPages.All)
        {
            data.Add(page);
        }

        return data;
    }

    /// <summary>The heading id Docusaurus gives a heading: github-slugger, with <c>{#custom-id}</c> and inline code handled.</summary>
    public static string Slug(string heading)
    {
        var custom = CustomId().Match(heading);
        if (custom.Success)
        {
            return custom.Groups["id"].Value;
        }

        var builder = new StringBuilder();
        foreach (var c in heading.Replace("`", string.Empty, StringComparison.Ordinal).ToLowerInvariant())
        {
            if (c == ' ')
            {
                builder.Append('-');
            }
            else if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    [Theory]
    [InlineData("`JevError` kinds", "jeverror-kinds")]
    [InlineData("Built at run time", "built-at-run-time")]
    [InlineData("C# notes", "c-notes")]
    [InlineData("What's new?", "whats-new")]
    [InlineData("Custom {#my-id}", "my-id")]
    public void Slug_FollowsTheDocusaurusRule(string heading, string expected) =>
        Assert.Equal(expected, Slug(heading));

    [Fact]
    public void Anchors_NumberRepeatedHeadings()
    {
        var ids = Anchors(["## Setup", "## Setup", "## Setup"]);
        Assert.Equal("setup,setup-1,setup-2", string.Join(',', ids));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryInternalLink_Resolves(string page)
    {
        var problems = new List<string>();
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, page));
        var own = Anchors(lines);
        var fenced = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                continue;
            }

            foreach (Match link in MarkdownLink().Matches(lines[i]))
            {
                var problem = Check(page, own, link.Groups["target"].Value);
                if (problem is not null)
                {
                    problems.Add($"{page}:{i + 1}: {problem}");
                }
            }
        }

        Assert.Empty(problems);
    }

    private static string? Check(string page, List<string> own, string target)
    {
        if (Scheme().IsMatch(target))
        {
            return null;
        }

        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var file = hash < 0 ? target : target[..hash];
        var anchor = hash < 0 ? null : target[(hash + 1)..];
        if (file.Length == 0)
        {
            return anchor is null || own.Contains(anchor, StringComparer.Ordinal) ? null : $"no heading for #{anchor}.";
        }

        var directory = Path.GetDirectoryName(Path.Combine(PublishedPages.Root, page))!;
        var resolved = Path.GetFullPath(Path.Combine(directory, file));
        if (!File.Exists(resolved))
        {
            return $"{file} does not exist.";
        }

        if (anchor is null || !resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Anchors(File.ReadAllLines(resolved)).Contains(anchor, StringComparer.Ordinal)
            ? null
            : $"{file} has no heading for #{anchor}.";
    }

    private static List<string> Anchors(IEnumerable<string> lines)
    {
        var ids = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var fenced = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            var heading = fenced ? null : Heading().Match(line);
            if (heading is not { Success: true })
            {
                continue;
            }

            var text = heading.Groups["text"].Value;
            var slug = Slug(text);
            if (CustomId().IsMatch(text))
            {
                ids.Add(slug);
            }
            else if (seen.TryGetValue(slug, out var count))
            {
                seen[slug] = count + 1;
                ids.Add($"{slug}-{count + 1}");
            }
            else
            {
                seen[slug] = 0;
                ids.Add(slug);
            }
        }

        return ids;
    }

    [GeneratedRegex(@"\{#(?<id>[^}\s]+)\}\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CustomId();

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.+?)\s*#*\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Heading();

    [GeneratedRegex(@"(?<!!)\[[^\]]*\]\((?<target>[^)\s]+)(?:\s+""[^""]*"")?\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*:", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Scheme();
}
