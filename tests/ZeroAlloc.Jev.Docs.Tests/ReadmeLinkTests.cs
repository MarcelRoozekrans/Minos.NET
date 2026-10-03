using System.Text.RegularExpressions;

namespace ZeroAlloc.Jev.Docs.Tests;

public sealed partial class ReadmeLinkTests
{
    private const string Site = "https://jev.zeroalloc.net";
    private const string Logo = "https://raw.githubusercontent.com/ZeroAlloc-Net/ZeroAlloc.Jev/main/assets/icon.png";

    private static readonly string[] ReadmeLines = File.ReadAllLines(Path.Combine(PublishedPages.Root, "README.md"));

    /// <summary>
    /// The route Docusaurus serves a page at: its folder plus its front-matter id, without an extension.
    /// A <c>slug</c> overrides that, and a page whose id equals its folder's name is the folder's index.
    /// </summary>
    public static string Route(string folder, IReadOnlyDictionary<string, string> frontMatter)
    {
        if (frontMatter.TryGetValue("slug", out var slug))
        {
            return slug;
        }

        var id = frontMatter["id"];
        if (folder.Length == 0)
        {
            return "/" + id;
        }

        var leaf = folder[(folder.LastIndexOf('/') + 1)..];
        return string.Equals(id, leaf, StringComparison.Ordinal) || string.Equals(id, "index", StringComparison.Ordinal)
            ? "/" + folder
            : $"/{folder}/{id}";
    }

    [Theory]
    [InlineData("", "getting-started", "/", "/")]
    [InlineData("", "diagnostics", null, "/diagnostics")]
    [InlineData("", "question-sets-at-run-time", null, "/question-sets-at-run-time")]
    [InlineData("patterns", "patterns", null, "/patterns")]
    [InlineData("patterns", "fan-out", null, "/patterns/fan-out")]
    [InlineData("patterns", "composite-scoring", null, "/patterns/composite-scoring")]
    public void Route_FollowsTheRoutesDocusaurusEmitted(string folder, string id, string? slug, string expected)
    {
        var frontMatter = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id };
        if (slug is not null)
        {
            frontMatter["slug"] = slug;
        }

        Assert.Equal(expected, Route(folder, frontMatter));
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
            else if (anchor.Length > 0 && !Markdown.Anchors(File.ReadAllLines(Path.Combine(PublishedPages.Root, page))).Contains(anchor))
            {
                problems.Add($"README.md:{line}: {target} has no heading with the id {anchor} in {page}.");
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
    public void TheReadme_PointsItsLogoAtTheAbsoluteRawUrl()
    {
        var sources = ReadmeLines
            .SelectMany(line => LogoSource().Matches(line).Select(m => m.Groups["src"].Value))
            .ToList();

        Assert.Equal([Logo], sources);
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
            var route = Route(folder, PublishedPages.FrontMatter(page));
            Assert.True(routes.TryAdd(route, page), $"{page} and {routes.GetValueOrDefault(route)} share the route {route}.");
        }

        return routes;
    }

    [GeneratedRegex(@"^https://github\.com/[^/]+/[^/]+/(?:blob|tree)/[^/]+/docs/(?<path>.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GuideInRepository();

    [GeneratedRegex(@"<img\b[^>]*\bsrc=""(?<src>[^""]*)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LogoSource();
}
