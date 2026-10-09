using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

public sealed class LinkTests
{
    private const string Page = "docs/patterns/fan-out.md";

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var page in PublishedPages.All)
        {
            data.Add(page);
        }

        return data;
    }

    /// <summary>The heading id Docusaurus gives a heading: github-slugger, with <c>{#custom-id}</c> and markup handled.</summary>
    public static string Slug(string heading) => Markdown.Slug(heading);

    [Theory]
    [InlineData("`JevError` kinds", "jeverror-kinds")]
    [InlineData("Built at run time", "built-at-run-time")]
    [InlineData("C# notes", "c-notes")]
    [InlineData("What's new?", "whats-new")]
    [InlineData("Custom {#my-id}", "my-id")]
    [InlineData("See [x](y.md)", "see-x")]
    [InlineData("**Bold** and *em* and _under_", "bold-and-em-and-under")]
    [InlineData("snake_case stays", "snake_case-stays")]
    [InlineData("Step 2 `x`", "step-2-x")]
    [InlineData("Phase 3.1 `a_1` and 2", "phase-31-a_1-and-2")]
    public void Slug_FollowsTheDocusaurusRule(string heading, string expected) =>
        Assert.Equal(expected, Slug(heading));

    [Fact]
    public void Anchors_NumberRepeatedHeadings()
    {
        var ids = Markdown.Anchors(["## Setup", "## Setup", "## Setup"]);
        Assert.Equal("setup,setup-1,setup-2", string.Join(',', ids));
    }

    [Fact]
    public void Anchors_CloseOnlyAFollowedHashSequence()
    {
        var ids = Markdown.Anchors(["## F# and C#", "## Closed ##", "## Closed too   ##  "]);
        Assert.Equal("f-and-c,closed,closed-too", string.Join(',', ids));
    }

    [Fact]
    public void Anchors_IgnoreHeadingsInCode()
    {
        var ids = Markdown.Anchors(["# Real", "~~~", "# Fake", "~~~"]);
        Assert.Equal("real", string.Join(',', ids));
    }

    [Fact]
    public void Links_SkipBacktickFences()
    {
        Assert.Equal("c.md", Targets("```", "[a](a.md)", "~~~", "[b](b.md)", "```", "[c](c.md)"));
    }

    [Fact]
    public void Links_SkipTildeFences()
    {
        Assert.Equal("c.md", Targets("~~~", "[a](a.md)", "```", "[b](b.md)", "~~~", "[c](c.md)"));
    }

    [Fact]
    public void Links_FenceClosesOnlyOnAnAtLeastAsLongMarker()
    {
        Assert.Equal("d.md", Targets("````", "[a](a.md)", "```", "[b](b.md)", "````", "[d](d.md)"));
    }

    [Fact]
    public void Links_SkipInlineCode()
    {
        Assert.Equal("real.md", Targets("Use `[a](nope.md)` and [b](real.md) and ``x `[c](no.md)` y``."));
    }

    [Fact]
    public void Links_IncludeImages()
    {
        Assert.Equal("a.png,b.md", Targets("![alt](a.png) and [t](b.md)"));
    }

    [Fact]
    public void Links_IncludeAnImageInsideALink()
    {
        Assert.Equal("outer.md,inner.png", Targets("[![alt](inner.png)](outer.md)"));
    }

    [Fact]
    public void Links_ParseAngleBracketTargets()
    {
        Assert.Equal("b c.md#x", Targets("[a](<b c.md#x>)"));
    }

    [Fact]
    public void Links_SkipFrontMatter()
    {
        Assert.Equal("a.md", Targets("---", "description: see [x](x.md)", "---", "[a](a.md)"));
    }

    [Fact]
    public void Check_AcceptsAnEncodedFileAndAnchor()
    {
        Assert.Null(LinkChecker.Check(Page, [], "fan%2Dout.md#built%2Dat%2Drun%2Dtime"));
    }

    [Fact]
    public void Check_RejectsASiteAbsoluteTarget()
    {
        var problem = LinkChecker.Check(Page, [], "/patterns/fan-out");
        Assert.Contains("relative link", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_RejectsATargetOutsideDocs()
    {
        var problem = LinkChecker.Check(Page, [], "../../README.md");
        Assert.Contains("outside docs/", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../planning/x.md")]
    [InlineData("../plans/x.md")]
    [InlineData("../superpowers/x.md")]
    public void Check_RejectsAnUnpublishedFolder(string target)
    {
        var problem = LinkChecker.Check(Page, [], target);
        Assert.Contains("unpublished", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_RejectsALinkWithTheWrongCase()
    {
        Assert.Null(LinkChecker.Check(Page, [], "fan-out.md"));
        Assert.NotNull(LinkChecker.Check(Page, [], "Fan-Out.md"));
        Assert.NotNull(LinkChecker.Check(Page, [], "../Patterns/fan-out.md"));
    }

    [Fact]
    public void Slug_KeepsUnderscoresInsideInlineCode()
    {
        // github-slugger lower-cases the rendered text and drops punctuation, so the dot goes and the underscores stay.
        Assert.Equal("_category_json", Slug("`_category_.json`"));
        Assert.Equal("a_b-c", Slug("`a_b` _c_"));
    }

    [Fact]
    public void Links_SkipFencesNestedInAListItem()
    {
        Assert.Equal("b.md", Targets("- item", "  ```cs", "  [a](a.md)", "  ```", "[b](b.md)"));
        Assert.Equal("b.md", Targets("1. item", "        ~~~", "        [a](a.md)", "   ~~~", "[b](b.md)"));
    }

    [Fact]
    public void Links_FollowATextThatWrapsAcrossLines()
    {
        Assert.Equal("x.md#h", Targets("See [Logging,", "traces and metrics](x.md#h) now."));
        Assert.Equal("b.md", Targets("Code `a", "[no](a.md)` then [b](b.md)"));
        Assert.Equal(string.Empty, Targets("One para [a](", "", "a.md) split"));
    }

    [Fact]
    public void Links_AllowOneLevelOfParenthesesInATarget()
    {
        Assert.Equal("a(1).md,b.md", Targets("[a](a(1).md) and [b](b.md)"));
        Assert.Equal("see-x", Slug("See [x](a(1).md)"));
    }

    [Fact]
    public void Check_RejectsAMissingPageAndAMissingAnchor()
    {
        Assert.NotNull(LinkChecker.Check(Page, [], "nope.md"));
        Assert.NotNull(LinkChecker.Check(Page, [], "fan-out.md#nope"));
        Assert.NotNull(LinkChecker.Check(Page, [], "#nope"));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryInternalLink_Resolves(string page)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, page));
        var own = Markdown.Anchors(lines);
        var problems = new List<string>();
        foreach (var (number, target) in Markdown.Links(lines))
        {
            var problem = LinkChecker.Check(page, own, target);
            if (problem is not null)
            {
                problems.Add($"{page}:{number}: {problem}");
            }
        }

        Assert.Empty(problems);
    }

    private static string Targets(params string[] lines) =>
        string.Join(',', Markdown.Links(lines).Select(l => l.Target));
}

/// <summary>Resolves one link target the way the docs site would.</summary>
internal static partial class LinkChecker
{
    public static string? Check(string page, IReadOnlyCollection<string> ownAnchors, string target)
    {
        if (Scheme().IsMatch(target))
        {
            return null;
        }

        if (target.StartsWith('/'))
        {
            return $"{target} is site-absolute; use a relative link.";
        }

        var hash = target.IndexOf('#', StringComparison.Ordinal);
        var file = Uri.UnescapeDataString(hash < 0 ? target : target[..hash]);
        var anchor = hash < 0 ? null : Uri.UnescapeDataString(target[(hash + 1)..]);
        if (file.Length == 0)
        {
            return anchor is null || ownAnchors.Contains(anchor, StringComparer.Ordinal) ? null : $"no heading for #{anchor}.";
        }

        var docs = Path.Combine(PublishedPages.Root, "docs");
        var directory = Path.GetDirectoryName(Path.Combine(PublishedPages.Root, page))!;
        var resolved = Path.GetFullPath(Path.Combine(directory, file));
        var relative = Path.GetRelativePath(docs, resolved).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return $"{file} resolves outside docs/.";
        }

        if (PublishedPages.IsUnpublished(relative))
        {
            return $"{file} is in an unpublished folder or is a README.";
        }

        if (!ExistsWithExactCase(docs, relative))
        {
            return $"{file} does not exist, or differs in letter case from the file on disk.";
        }

        if (anchor is null || !resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Markdown.Anchors(File.ReadAllLines(resolved)).Contains(anchor, StringComparer.Ordinal)
            ? null
            : $"{file} has no heading for #{anchor}.";
    }

    // File.Exists ignores case on Windows, but the site is built on Linux, so every segment must match its directory entry exactly.
    private static bool ExistsWithExactCase(string docs, string relative)
    {
        var current = docs;
        foreach (var segment in relative.Split('/'))
        {
            if (!Directory.Exists(current)
                || !Array.Exists(Directory.GetFileSystemEntries(current), e => string.Equals(Path.GetFileName(e), segment, StringComparison.Ordinal)))
            {
                return false;
            }

            current = Path.Combine(current, segment);
        }

        return true;
    }

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9+.-]*:", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Scheme();
}
