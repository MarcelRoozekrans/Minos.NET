using System.Globalization;
using System.Text.Json;

namespace Minos.Docs.Tests;

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

    public static TheoryData<string> Categories()
    {
        var data = new TheoryData<string>();
        foreach (var category in PublishedPages.Categories)
        {
            data.Add(category);
        }

        return data;
    }

    [Fact]
    public void PublishedPages_AreFound()
    {
        Assert.Contains("docs/performance.md", PublishedPages.All);
        Assert.Contains("docs/patterns/fan-out.md", PublishedPages.All);
        Assert.Contains("docs/patterns/_category_.json", PublishedPages.Categories);
        Assert.DoesNotContain(PublishedPages.All, p =>
            p.StartsWith("docs/planning/", StringComparison.Ordinal)
            || p.StartsWith("docs/plans/", StringComparison.Ordinal)
            || p.StartsWith("docs/superpowers/", StringComparison.Ordinal)
            || p.StartsWith("docs/design/", StringComparison.Ordinal));
    }

    [Fact]
    public void FrontMatter_NeedsAClosingFence()
    {
        Assert.Empty(PublishedPages.FrontMatter(["---", "id: a", "title: A", "", "# A"]));
        Assert.Equal("a", PublishedPages.FrontMatter(["---", "id: a", "---", "# A"])["id"]);
    }

    [Fact]
    public void FrontMatter_StopsAtTheClosingFence()
    {
        var front = PublishedPages.FrontMatter(["---", "id: a", "---", "note: body text", "# A"]);
        Assert.False(front.ContainsKey("note"));
    }

    [Fact]
    public void Positions_PutACategoryInTheFolderThatContainsIt()
    {
        Assert.Equal("docs", CategoryFolder("docs/patterns/_category_.json"));
        Assert.Equal("docs/guide", CategoryFolder("docs/guide/deep/_category_.json"));
        Assert.Equal("docs", CategoryFolder("docs/_category_.json"));
    }

    [Fact]
    public void Title_StripsMarkupFromTheHeading()
    {
        Assert.Equal("F# and C#", Markdown.FirstH1(["# F# and C#"]));
        Assert.Equal("Bold em and code", Markdown.FirstH1(["# **Bold** *em* and `code` #"]));
    }

    [Fact]
    public void FrontMatter_StripsMatchingQuotes()
    {
        var front = PublishedPages.FrontMatter(["---", "slug: \"/\"", "description: ''", "title: 'It's'", "mixed: \"a'", "---"]);
        Assert.Equal("/", front["slug"]);
        Assert.Equal(string.Empty, front["description"]);
        Assert.Equal("It's", front["title"]);
        Assert.Equal("\"a'", front["mixed"]);
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

    [Theory]
    [MemberData(nameof(Pages))]
    public void Title_IsTheFirstHeading(string page)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, page));
        Assert.Equal(Markdown.FirstH1(lines), Field(page, "title"));
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
        var entries = new List<(string Owner, string Folder, int Position)>();
        foreach (var page in PublishedPages.All)
        {
            if (int.TryParse(Field(page, "sidebar_position"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var position))
            {
                entries.Add((page, Parent(page), position));
            }
        }

        // A category sits in the folder that contains its own folder, next to that folder's pages.
        foreach (var category in PublishedPages.Categories)
        {
            if (CategoryPosition(File.ReadAllText(Path.Combine(PublishedPages.Root, category))) is { } position)
            {
                entries.Add((category, CategoryFolder(category), position));
            }
        }

        var duplicates = entries
            .GroupBy(e => (e.Folder, e.Position))
            .Where(g => g.Count() > 1)
            .Select(g => $"sidebar position {g.Key.Position} in {g.Key.Folder} is used by {string.Join(", ", g.Select(x => x.Owner))}")
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
        Assert.Equal("/", Field(GettingStarted, "slug"));
    }

    [Theory]
    [MemberData(nameof(Categories))]
    public void EveryCategory_IsValid(string category)
    {
        Assert.Null(CategoryProblem(File.ReadAllText(Path.Combine(PublishedPages.Root, category))));
    }

    [Theory]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("{ \"position\": 1 }", "label")]
    [InlineData("{ \"label\": 3, \"position\": 1 }", "label")]
    [InlineData("{ \"label\": \"\", \"position\": 1 }", "label")]
    [InlineData("{ \"label\": \"A\" }", "position")]
    [InlineData("{ \"label\": \"A\", \"position\": \"1\" }", "position")]
    [InlineData("{ \"label\": \"A\", \"position\": 1.5 }", "position")]
    public void CategoryProblem_FindsEachFault(string json, string expected)
    {
        Assert.Contains(expected, CategoryProblem(json), StringComparison.Ordinal);
    }

    [Fact]
    public void CategoryProblem_AcceptsAValidCategory()
    {
        Assert.Null(CategoryProblem("{ \"label\": \"A\", \"position\": 2 }"));
    }

    private static string? CategoryProblem(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "not a JSON object.";
            }

            if (!document.RootElement.TryGetProperty("label", out var label)
                || label.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(label.GetString()))
            {
                return "label is missing or not a non-empty string.";
            }

            return CategoryPosition(json) is null ? "position is missing or not an integer." : null;
        }
        catch (JsonException)
        {
            return "not valid JSON.";
        }
    }

    private static int? CategoryPosition(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("position", out var position)
                && position.ValueKind == JsonValueKind.Number
                && position.TryGetInt32(out var value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string CategoryFolder(string category)
    {
        var folder = Parent(category);
        return folder.Contains('/', StringComparison.Ordinal) ? Parent(folder) : folder;
    }

    private static string Parent(string path) => path[..path.LastIndexOf('/')];

    private static string Field(string page, string key) =>
        PublishedPages.FrontMatter(page).TryGetValue(key, out var value) ? value : string.Empty;
}
