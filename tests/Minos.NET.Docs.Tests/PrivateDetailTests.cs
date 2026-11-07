using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The Escalation pages and the proposal draft are public. They name no private workload, host, path, address or key.
public sealed partial class PrivateDetailTests
{
    private static readonly string[] Words = ["Daedalus", "Thalos", "/home/", @"C:\Users"];

    public static TheoryData<string> Files()
    {
        var files = new TheoryData<string>();
        foreach (var page in Directory.GetFiles(Path.Combine(PublishedPages.Root, "docs", "escalation"), "*.md").Order(StringComparer.Ordinal))
        {
            files.Add(Path.GetRelativePath(PublishedPages.Root, page).Replace('\\', '/'));
        }

        if (File.Exists(Path.Combine(PublishedPages.Root, DraftProposalTests.Draft)))
        {
            files.Add(DraftProposalTests.Draft);
        }

        return files;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryEscalationPage_HasNoPrivateDetail(string file) =>
        Assert.Empty(Findings(File.ReadAllText(Path.Combine(PublishedPages.Root, file))));

    [Theory]
    [InlineData("the Daedalus workload")]
    [InlineData("the THALOS host")]
    [InlineData("under /home/someone/work")]
    [InlineData(@"in C:\Users\someone")]
    [InlineData("write to someone@example.org")]
    [InlineData("key sk-abcdefghijklmnop1234")]
    public void TheDenylist_CatchesEachKind(string sample) => Assert.NotEmpty(Findings(sample));

    // Every host a page or the draft links must be one of these. A new host is added here only when a page legitimately links it.
    private static readonly string[] AllowedHosts =
    [
        "github.com", "learn.microsoft.com", "developers.openai.com", "docs.typesafe.ai", "developers.cloudflare.com", "marcelroozekrans.github.io",
    ];

    [Theory]
    [MemberData(nameof(Files))]
    public void EveryLinkedHost_IsOnTheAllowlist(string file) =>
        Assert.Empty(UnknownHosts(File.ReadAllText(Path.Combine(PublishedPages.Root, file))));

    [Fact]
    public void TheAllowlist_CatchesAnUnknownHost()
    {
        Assert.Equal(["private.example.net"], UnknownHosts("see https://private.example.net/path and https://github.com/x/y"));
        Assert.Empty(UnknownHosts("see https://github.com/x/y and (https://learn.microsoft.com/dotnet)"));
    }

    private static List<string> UnknownHosts(string text) =>
        UrlHost().Matches(text)
            .Select(match => match.Groups["host"].Value.ToLowerInvariant())
            .Where(host => !AllowedHosts.Contains(host, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void TheDenylist_LeavesOrdinaryTextAlone() =>
        Assert.Empty(Findings("Escalate to the fallback at 0.85, a sk- prefix alone is fine, and so is user@ with no domain."));

    private static List<string> Findings(string text)
    {
        var found = new List<string>();
        foreach (var word in Words)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(word);
            }
        }

        if (EmailAddress().IsMatch(text))
        {
            found.Add("an e-mail address");
        }

        if (SecretKey().IsMatch(text))
        {
            found.Add("an API key");
        }

        return found;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"https?://(?<host>[A-Za-z0-9.-]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UrlHost();

    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{16,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretKey();
}
