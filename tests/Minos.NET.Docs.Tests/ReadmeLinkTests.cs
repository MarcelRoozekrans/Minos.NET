using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

public sealed partial class ReadmeLinkTests
{
    private const string Site = "https://jev.zeroalloc.net";
    private const string Logo = "https://raw.githubusercontent.com/MarcelRoozekrans/Minos.NET/main/assets/icon.png";

    private static readonly string[] ReadmeLines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "README.md"));

    /// <summary>
    /// The route Docusaurus serves a page at. A folder's index is decided by the file name, not the id: <c>index</c>,
    /// <c>README</c> or a file named after its folder serves at the folder's route. Any other page serves at its folder
    /// plus its front-matter id. A <c>slug</c> starting with <c>/</c> is the route as written, and any other slug
    /// resolves against the folder.
    /// </summary>
    public static string Route(string folder, string fileName, IReadOnlyDictionary<string, string> frontMatter)
    {
        var prefix = folder.Length == 0 ? string.Empty : "/" + folder;
        if (frontMatter.TryGetValue("slug", out var slug))
        {
            return slug.StartsWith('/') ? slug : $"{prefix}/{slug}";
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var leaf = folder[(folder.LastIndexOf('/') + 1)..];
        var isIndex = string.Equals(name, "index", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "README", StringComparison.OrdinalIgnoreCase)
            || (leaf.Length > 0 && string.Equals(name, leaf, StringComparison.OrdinalIgnoreCase));
        if (isIndex)
        {
            return prefix.Length == 0 ? "/" : prefix;
        }

        return $"{prefix}/{frontMatter["id"]}";
    }

    [Theory]
    [InlineData("", "getting-started.md", "getting-started", "/", "/")]
    [InlineData("", "diagnostics.md", "diagnostics", null, "/diagnostics")]
    [InlineData("", "question-sets-at-run-time.md", "question-sets-at-run-time", null, "/question-sets-at-run-time")]
    [InlineData("patterns", "index.md", "patterns", null, "/patterns")]
    [InlineData("patterns", "fan-out.md", "fan-out", null, "/patterns/fan-out")]
    [InlineData("patterns", "composite-scoring.md", "composite-scoring", null, "/patterns/composite-scoring")]
    [InlineData("patterns", "index.md", "overview", null, "/patterns")]
    [InlineData("patterns", "patterns.md", "overview", null, "/patterns")]
    [InlineData("patterns", "README.md", "overview", null, "/patterns")]
    [InlineData("patterns", "Patterns.md", "overview", null, "/patterns")]
    [InlineData("patterns", "fan-out.md", "fan-out", "wide", "/patterns/wide")]
    [InlineData("", "diagnostics.md", "diagnostics", "rules", "/rules")]
    [InlineData("patterns", "fan-out.md", "fan-out", "/elsewhere/fan-out", "/elsewhere/fan-out")]
    public void Route_FollowsTheRoutesDocusaurusEmitted(string folder, string fileName, string id, string? slug, string expected)
    {
        var frontMatter = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id };
        if (slug is not null)
        {
            frontMatter["slug"] = slug;
        }

        Assert.Equal(expected, Route(folder, fileName, frontMatter));
    }

    [Fact]
    public void Anchors_CanLeaveOutTheH1()
    {
        string[] lines = ["# Title", "## One", "### Two", "## One"];

        Assert.Equal(["title", "one", "two", "one-1"], Markdown.Anchors(lines));
        Assert.Equal(["one", "two", "one-1"], Markdown.Anchors(lines, minLevel: 2));
    }

    [Fact]
    public void EverySiteLink_ResolvesToAPublishedPageAndAnchor()
    {
        var routes = PageRoutes();
        var problems = new List<string>();
        var count = 0;
        foreach (var (line, target) in SiteLinks())
        {
            count++;
            var (path, anchor) = Split(target);
            if (!routes.TryGetValue(path, out var page))
            {
                problems.Add($"README.md:{line}: {target} is not the route of a published page.");
            }
            else if (anchor.Length > 0 && !Markdown.Anchors(File.ReadAllLines(Path.Combine(PublishedPages.Root, page)), minLevel: 2).Contains(anchor))
            {
                problems.Add($"README.md:{line}: {target} has no h2 to h6 heading with the id {anchor} in {page}.");
            }
        }

        Assert.True(count > 0, "The README links no page of the site.");
        Assert.Empty(problems);
    }

    [Fact]
    public void TheReadme_LinksEveryPublishedPageOnce()
    {
        var routes = PageRoutes();

        // The Documentation section is the list of pages. The site's root is Getting started, so the intro sentence
        // and the Install note may link it as well; only the bullets count.
        var linked = DocumentationBullets()
            .SelectMany(bullet => Markdown.Links([bullet]))
            .Where(l => l.Target.StartsWith(Site, StringComparison.Ordinal))
            .Select(l => Split(l.Target).Path)
            .ToList();

        var problems = new List<string>();
        foreach (var (route, page) in routes)
        {
            var times = linked.Count(l => string.Equals(l, route, StringComparison.Ordinal));
            if (times != 1)
            {
                problems.Add($"{page} ({route}) is linked {times} times in the Documentation list; the README links each page once.");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void TheReadme_HasNoRelativeOrRepositoryLinksToTheGuide()
    {
        var problems = Markdown.Links(ReadmeLines)
            .Where(l => l.Target.StartsWith("docs/", StringComparison.Ordinal)
                || l.Target.StartsWith("./docs/", StringComparison.Ordinal)
                || IsGuideInRepository(l.Target))
            .Select(l => $"README.md:{l.Line}: {l.Target} links the guide through the repository; link the site.")
            .ToList();

        Assert.Empty(problems);
    }

    [Fact]
    public void TheReadme_ShowsItsLogoAsAMarkdownImageAtTheAbsoluteRawUrl()
    {
        var images = ReadmeLines
            .Select(line => LogoImage().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups["src"].Value)
            .ToList();

        Assert.Equal([Logo], images);
    }

    // nuget.org shows a package README with raw HTML switched off, so a tag would appear as literal text.
    [Fact]
    public void TheReadme_HasNoRawHtmlOutsideCode()
    {
        var problems = Markdown.Prose(ReadmeLines)
            .Select(l => (l.Number, Text: CodeSpan().Replace(l.Text, string.Empty)))
            .Where(l => HtmlTag().IsMatch(l.Text))
            .Select(l => $"README.md:{l.Number}: {l.Text.Trim()} holds a raw HTML tag; use Markdown.")
            .ToList();

        Assert.Empty(problems);
    }

    // The README is packed into the NuGet package, and nuget.org cannot resolve a relative path.
    [Fact]
    public void EveryReadmeLink_IsAbsolute()
    {
        var problems = Markdown.Links(ReadmeLines)
            .Where(l => !l.Target.StartsWith("https://", StringComparison.Ordinal)
                && !l.Target.StartsWith("http://", StringComparison.Ordinal)
                && !l.Target.StartsWith('#'))
            .Select(l => $"README.md:{l.Line}: {l.Target} is not an absolute http or https URL.")
            .ToList();

        Assert.Empty(problems);
    }

    [Fact]
    public void TheDocumentationBullets_FollowTheSidebarAndTitleEachPage()
    {
        var routes = PageRoutes();
        var bullets = DocumentationBullets()
            .Select(b => BulletLink().Match(b))
            .ToList();
        Assert.All(bullets, m => Assert.True(m.Success, "A Documentation bullet is not '- [title](url): summary'."));

        var problems = new List<string>();
        var order = new List<string>();
        foreach (ref readonly var bullet in CollectionsMarshal.AsSpan(bullets))
        {
            var route = Split(bullet.Groups["url"].Value).Path;
            order.Add(route);
            if (routes.TryGetValue(route, out var page))
            {
                var title = PublishedPages.FrontMatter(page)["title"];
                if (!string.Equals(bullet.Groups["text"].Value, title, StringComparison.Ordinal))
                {
                    problems.Add($"{page}: the bullet text '{bullet.Groups["text"].Value}' is not the title '{title}'.");
                }
            }
        }

        Assert.Empty(problems);
        Assert.Equal(SidebarOrder(routes), order);
    }

    // Top-level pages and folders sort by sidebar_position and the folder's _category_.json position. Inside a
    // folder the pages sort by their own sidebar_position, so the index page, at 1, leads its folder.
    private static List<string> SidebarOrder(Dictionary<string, string> routes) =>
        [.. routes
            .Select(r => (Route: r.Key, Key: SidebarKey(r.Value)))
            .OrderBy(r => r.Key.Top)
            .ThenBy(r => r.Key.Inner)
            .Select(r => r.Route)];

    private static (int Top, int Inner) SidebarKey(string page)
    {
        var position = int.Parse(PublishedPages.FrontMatter(page)["sidebar_position"], System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.GetDirectoryName(page["docs/".Length..])!.Replace('\\', '/');
        if (folder.Length == 0)
        {
            return (position, 0);
        }

        Assert.DoesNotContain('/', folder);
        var category = File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", folder, "_category_.json"));
        var top = int.Parse(CategoryPosition().Match(category).Groups["position"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (top, position);
    }

    // A repository link into docs/ is a guide link unless it points at an unpublished folder, such as the roadmap in planning/.
    private static bool IsGuideInRepository(string target)
    {
        var match = GuideInRepository().Match(target);
        return match.Success && !PublishedPages.IsUnpublished(match.Groups["path"].Value);
    }

    private static List<string> DocumentationBullets()
    {
        var start = Array.FindIndex(ReadmeLines, l => string.Equals(l, "## Documentation", StringComparison.Ordinal));
        Assert.True(start >= 0, "The README has no Documentation section.");
        return [.. ReadmeLines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)).Where(l => l.StartsWith("- ", StringComparison.Ordinal))];
    }

    private static List<(int Line, string Target)> SiteLinks() =>
        [.. Markdown.Links(ReadmeLines).Where(l => l.Target.StartsWith(Site, StringComparison.Ordinal))];

    private static (string Path, string Anchor) Split(string target)
    {
        var rest = target[Site.Length..];
        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        var path = hash < 0 ? rest : rest[..hash];
        return (path.Length == 0 ? "/" : path.TrimEnd('/') is { Length: > 0 } trimmed ? trimmed : "/", hash < 0 ? string.Empty : rest[(hash + 1)..]);
    }

    private static Dictionary<string, string> PageRoutes()
    {
        var routes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var page in PublishedPages.All)
        {
            var folder = Path.GetDirectoryName(page["docs/".Length..])!.Replace('\\', '/');
            var route = Route(folder, Path.GetFileName(page), PublishedPages.FrontMatter(page));
            Assert.True(routes.TryAdd(route, page), $"{page} and {routes.GetValueOrDefault(route)} share the route {route}.");
        }

        return routes;
    }

    [GeneratedRegex(@"^https://github\.com/[^/]+/[^/]+/(?:blob|tree)/[^/]+/docs/(?<path>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GuideInRepository();

    [GeneratedRegex(@"^!\[Minos\.NET\]\((?<src>[^)]+)\)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LogoImage();

    [GeneratedRegex(@"`[^`]*`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"<[A-Za-z/][^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"^- \[(?<text>[^\]]+)\]\((?<url>[^)]+)\)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BulletLink();

    [GeneratedRegex(@"""position""\s*:\s*(?<position>[0-9]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CategoryPosition();
}
