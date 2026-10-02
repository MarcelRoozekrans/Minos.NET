using System.Globalization;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class FrontMatterTests
{
    private const string GettingStarted = "docs/getting-started.md";

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var page in PublishedPages.All)
        {
            data.Add(page);
        }

        return data;
    }

    [Fact]
    public void PublishedPages_AreFound()
    {
        Assert.Contains("docs/performance.md", PublishedPages.All);
        Assert.Contains("docs/patterns/fan-out.md", PublishedPages.All);
        Assert.DoesNotContain(PublishedPages.All, p =>
            p.StartsWith("docs/planning/", StringComparison.Ordinal)
            || p.StartsWith("docs/plans/", StringComparison.Ordinal)
            || p.StartsWith("docs/superpowers/", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryPage_HasTheRequiredFields(string page)
    {
        var front = PublishedPages.FrontMatter(page);
        foreach (var key in new[] { "id", "title", "sidebar_position", "description" })
        {
            Assert.True(front.TryGetValue(key, out var value) && value.Length > 0, $"{page} has no {key} in its front matter.");
        }

        Assert.True(
            int.TryParse(front["sidebar_position"], NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            $"{page}: sidebar_position '{front["sidebar_position"]}' is not an integer.");
    }

    [Fact]
    public void Ids_AreUnique()
    {
        var duplicates = PublishedPages.All
            .Select(p => (Page: p, Id: Field(p, "id")))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"id '{g.Key}' is used by {string.Join(", ", g.Select(x => x.Page))}")
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Positions_AreUniqueWithinAFolder()
    {
        var duplicates = PublishedPages.All
            .Select(p => (Page: p, Folder: p[..p.LastIndexOf('/')], Position: Field(p, "sidebar_position")))
            .GroupBy(x => (x.Folder, x.Position))
            .Where(g => g.Count() > 1)
            .Select(g => $"sidebar_position {g.Key.Position} in {g.Key.Folder} is used by {string.Join(", ", g.Select(x => x.Page))}")
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void OnlyGettingStarted_IsTheRoot()
    {
        var roots = PublishedPages.All
            .Where(p => string.Equals(Field(p, "slug"), "/", StringComparison.Ordinal))
            .Where(p => !string.Equals(p, GettingStarted, StringComparison.Ordinal))
            .ToList();
        Assert.Empty(roots);
    }

    private static string Field(string page, string key) =>
        PublishedPages.FrontMatter(page).TryGetValue(key, out var value) ? value : string.Empty;
}
