namespace ZeroAlloc.Jev.Docs.Tests;

/// <summary>The pages the docs site publishes: every <c>docs/**/*.md</c> except the planning folders and README files.</summary>
internal static class PublishedPages
{
    private static readonly string[] Unpublished = ["planning", "plans", "superpowers"];

    public static string Root { get; } = FindRoot();

    public static IReadOnlyList<string> All { get; } = Find();

    /// <summary>Reads the <c>---</c>-fenced front matter at the top of a page as flat <c>key: value</c> pairs.</summary>
    public static IReadOnlyDictionary<string, string> FrontMatter(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(Root, relativePath));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (lines.Length == 0 || !string.Equals(lines[0], "---", StringComparison.Ordinal))
        {
            return result;
        }

        var opening = true;
        foreach (var line in lines)
        {
            if (opening)
            {
                opening = false;
                continue;
            }

            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                break;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                result[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        return result;
    }

    private static string[] Find()
    {
        var docs = Path.Combine(Root, "docs");
        var pages = new List<string>();
        foreach (var path in Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(docs, path).Replace('\\', '/');
            var top = relative.Split('/')[0];
            if (Array.Exists(Unpublished, u => string.Equals(u, top, StringComparison.Ordinal))
                || string.Equals(Path.GetFileName(path), "README.md", StringComparison.Ordinal))
            {
                continue;
            }

            pages.Add("docs/" + relative);
        }

        pages.Sort(StringComparer.Ordinal);
        return [.. pages];
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Jev.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
