namespace Minos.Docs.Tests;

public sealed class SnippetSourceTests
{
    private const string SnippetStart = "<!-- snippet: ";
    private const string SnippetEnd = "<!-- endSnippet -->";
    private const string Fence = "```";

    // mdsnippets reads a region from any tracked text file, not only this project. A second file with the same
    // region name adds a second code block under the same snippet, and the drift check still passes, because the
    // committed Markdown is what the tool regenerates. So every generated block must hold exactly one code block.
    [Fact]
    public void EverySnippet_HasExactlyOneSource()
    {
        var guides = Directory.GetFiles(Path.Combine(RepositoryRoot(), "docs", "patterns"), "*.md");
        Assert.NotEmpty(guides);

        var snippets = 0;
        var problems = new List<string>();
        foreach (var guide in guides)
        {
            var lines = File.ReadAllLines(guide);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith(SnippetStart, StringComparison.Ordinal))
                {
                    continue;
                }

                snippets++;
                var name = lines[i][SnippetStart.Length..].Replace("-->", string.Empty, StringComparison.Ordinal).Trim();
                var fences = 0;
                var end = i + 1;
                for (; end < lines.Length && !lines[end].StartsWith(SnippetEnd, StringComparison.Ordinal); end++)
                {
                    if (lines[end].StartsWith(Fence, StringComparison.Ordinal))
                    {
                        fences++;
                    }
                }

                // One code block is an opening and a closing fence.
                if (end == lines.Length || fences != 2)
                {
                    problems.Add($"{Path.GetFileName(guide)}: snippet {name} has {fences / 2} code blocks{(end == lines.Length ? " and no end marker" : string.Empty)}.");
                }

                i = end;
            }
        }

        Assert.True(snippets > 0, "No snippets were found under docs/patterns.");
        Assert.Empty(problems);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Minos.NET.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No directory above {AppContext.BaseDirectory} contains Minos.NET.slnx.");
    }
}
