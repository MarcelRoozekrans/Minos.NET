namespace Minos.Docs.Tests;

/// <summary>The pages the docs site publishes: every <c>docs/**/*.md</c> except the planning folders, the brand record in design/, and README files.</summary>
internal static class PublishedPages
{
    private static readonly string[] Unpublished = ["design", "planning", "plans", "superpowers"];

    public static string Root { get; } = FindRoot();

    public static IReadOnlyList<string> All { get; } = Find();

    /// <summary>The <c>_category_.json</c> files of the published folders, as repository-relative paths.</summary>
    public static IReadOnlyList<string> Categories { get; } = FindCategories();

    /// <summary>Whether a path relative to <c>docs/</c> is in an unpublished folder or is a README.</summary>
    public static bool IsUnpublished(string relativeToDocs) =>
        Array.Exists(Unpublished, u => relativeToDocs.StartsWith(u + "/", StringComparison.Ordinal))
        || string.Equals(Path.GetFileName(relativeToDocs), "README.md", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads the <c>---</c>-fenced front matter at the top of a page as flat <c>key: value</c> pairs.</summary>
    public static IReadOnlyDictionary<string, string> FrontMatter(string relativePath) =>
        FrontMatter(File.ReadAllLines(Path.Combine(Root, relativePath)));

    /// <summary>Reads front matter from lines. A block with no closing <c>---</c> is no front matter. Matching surrounding quotes are removed.</summary>
    public static IReadOnlyDictionary<string, string> FrontMatter(IReadOnlyCollection<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var length = Markdown.FrontMatterLength(lines);
        var number = 0;
        foreach (var line in lines)
        {
            number++;
            if (number == 1)
            {
                continue;
            }

            if (number >= length)
            {
                break;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                result[line[..colon].Trim()] = Unquote(line[(colon + 1)..].Trim());
            }
        }

        return result;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0] ? value[1..^1] : value;

    private static string[] Find()
    {
        var docs = Path.Combine(Root, "docs");
        var pages = new List<string>();
        foreach (var path in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(docs, path).Replace('\\', '/');
            if (!IsUnpublished(relative))
            {
                pages.Add("docs/" + relative);
            }
        }

        pages.Sort(StringComparer.Ordinal);
        return [.. pages];
    }

    private static string[] FindCategories()
    {
        var docs = Path.Combine(Root, "docs");
        var found = new List<string>();
        foreach (var path in Directory.EnumerateFiles(docs, "_category_.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(docs, path).Replace('\\', '/');
            if (!IsUnpublished(relative))
            {
                found.Add("docs/" + relative);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return [.. found];
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Minos.NET.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
