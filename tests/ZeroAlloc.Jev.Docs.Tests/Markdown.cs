using System.Text;
using System.Text.RegularExpressions;

namespace ZeroAlloc.Jev.Docs.Tests;

/// <summary>Just enough Markdown reading for the docs checks: prose lines, headings and their ids, and links.</summary>
internal static partial class Markdown
{
    // Private-use characters that wrap a code-span placeholder, so no heading text can collide with it.
    private const char SpanOpen = '\uE000';
    private const char SpanClose = '\uE001';

    /// <summary>The number of lines a closed <c>---</c> front-matter block takes, including both fences, or 0 when there is none.</summary>
    public static int FrontMatterLength(IEnumerable<string> lines)
    {
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (number == 1)
            {
                if (!string.Equals(line, "---", StringComparison.Ordinal))
                {
                    return 0;
                }
            }
            else if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                return number;
            }
        }

        return 0;
    }

    /// <summary>The lines outside front matter and fenced code, with their 1-based line numbers.</summary>
    public static List<(int Number, string Text)> Prose(IEnumerable<string> lines)
    {
        var all = lines as IReadOnlyCollection<string> ?? [.. lines];
        var skip = FrontMatterLength(all);
        var result = new List<(int, string)>();
        var marker = '\0';
        var length = 0;
        var number = 0;
        foreach (var line in all)
        {
            number++;
            if (number <= skip)
            {
                continue;
            }

            var fence = Fence().Match(line);
            var run = fence.Success ? fence.Groups["run"].Value : string.Empty;
            var rest = fence.Success ? fence.Groups["rest"].Value : string.Empty;
            if (marker == '\0')
            {
                if (fence.Success && !(run[0] == '`' && rest.Contains('`', StringComparison.Ordinal)))
                {
                    marker = run[0];
                    length = run.Length;
                    continue;
                }

                result.Add((number, line));
            }
            else if (fence.Success && run[0] == marker && run.Length >= length && rest.Trim().Length == 0)
            {
                marker = '\0';
            }
        }

        return result;
    }

    /// <summary>The heading id Docusaurus gives a heading: github-slugger, with <c>{#custom-id}</c> and markup handled.</summary>
    public static string Slug(string heading)
    {
        var custom = CustomId().Match(heading);
        if (custom.Success)
        {
            return custom.Groups["id"].Value;
        }

        var builder = new StringBuilder();
        foreach (var c in PlainText(heading).ToLowerInvariant())
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

    /// <summary>The heading text as rendered: no <c>{#id}</c>, links reduced to their text, no emphasis marks, code as its content.</summary>
    public static string PlainText(string heading)
    {
        // Code spans become their literal content and are shielded from the markup stripping, so `_category_.json` keeps its underscores.
        var spans = new List<string>();
        var text = CodeSpan().Replace(
            CustomId().Replace(heading, string.Empty),
            match =>
            {
                spans.Add(match.Groups["content"].Value);
                return $"{SpanOpen}{spans.Count - 1}{SpanClose}";
            });
        text = LinkOrImage().Replace(text, "${text}");
        text = text.Replace("*", string.Empty, StringComparison.Ordinal);
        text = EmphasisUnderscore().Replace(text, string.Empty);
        return Placeholder().Replace(text, match => spans[int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture)]).Trim();
    }

    /// <summary>The first level-1 heading of a page, as rendered, or null.</summary>
    public static string? FirstH1(IEnumerable<string> lines)
    {
        foreach (var (_, text) in Prose(lines))
        {
            var match = H1().Match(text);
            if (match.Success)
            {
                return PlainText(match.Groups["text"].Value);
            }
        }

        return null;
    }

    /// <summary>The ids of a page's headings, with <c>-1</c>, <c>-2</c> added to repeats.</summary>
    public static List<string> Anchors(IEnumerable<string> lines)
    {
        var ids = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, line) in Prose(lines))
        {
            var heading = Heading().Match(line);
            if (!heading.Success)
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

    /// <summary>Every link and image target in the prose, outside code, with the line number its paragraph starts on.</summary>
    public static List<(int Line, string Target)> Links(IEnumerable<string> lines)
    {
        var result = new List<(int, string)>();
        var start = 0;
        var previous = 0;
        var paragraph = new StringBuilder();
        foreach (var (number, text) in Prose(lines))
        {
            var continues = paragraph.Length > 0 && number == previous + 1 && text.Trim().Length > 0;
            if (!continues)
            {
                Flush(paragraph, start, result);
                start = number;
            }

            if (text.Trim().Length > 0)
            {
                paragraph.Append(text).Append(' ');
            }

            previous = number;
        }

        Flush(paragraph, start, result);
        return result;
    }

    private static void Flush(StringBuilder paragraph, int start, List<(int, string)> result)
    {
        if (paragraph.Length > 0)
        {
            Collect(CodeSpan().Replace(paragraph.ToString(), " "), start, result);
            paragraph.Clear();
        }
    }

    private static void Collect(string text, int number, List<(int, string)> result)
    {
        foreach (Match link in Link().Matches(text))
        {
            result.Add((number, link.Groups["angle"].Success ? link.Groups["angle"].Value : link.Groups["target"].Value));
            Collect(link.Groups["text"].Value, number, result);
        }
    }

    [GeneratedRegex(@"^\s*(?<run>`{3,}|~{3,})(?<rest>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Fence();

    [GeneratedRegex(@"\s*\{#(?<id>[^}\s]+)\}\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CustomId();

    [GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Heading();

    [GeneratedRegex(@"^ {0,3}#[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex H1();

    [GeneratedRegex(@"!?\[(?<text>[^\]]*)\]\((?:[^()]|\([^()]*\))*\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkOrImage();

    [GeneratedRegex("\uE000(?<index>[0-9]+)\uE001", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"(?<!\w)_+|_+(?!\w)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmphasisUnderscore();

    [GeneratedRegex(@"(?<ticks>`+)(?!`)(?<content>.+?)(?<!`)\k<ticks>(?!`)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\[(?<text>(?:[^\[\]]|\[[^\]]*\])*)\]\((?:<(?<angle>[^>]*)>|(?<target>(?:[^()\s]|\([^()\s]*\))+))(?:\s+""[^""]*"")?\s*\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Link();
}
