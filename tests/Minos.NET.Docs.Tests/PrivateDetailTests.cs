using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The Escalation pages and the proposal draft are public. They name no private workload, host, path, address or key.
public sealed partial class PrivateDetailTests
{
    private const string Draft = "docs/plans/2026-11-06-dotnet-extensions-decision-proposal-draft.md";

    private static readonly string[] Words = ["Daedalus", "Thalos", "maintainer-domain.invalid", "/home/", @"C:\Users"];

    public static TheoryData<string> Files()
    {
        var files = new TheoryData<string>();
        foreach (var page in Directory.GetFiles(Path.Combine(PublishedPages.Root, "docs", "escalation"), "*.md").Order(StringComparer.Ordinal))
        {
            files.Add(Path.GetRelativePath(PublishedPages.Root, page).Replace('\\', '/'));
        }

        if (File.Exists(Path.Combine(PublishedPages.Root, Draft)))
        {
            files.Add(Draft);
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
    [InlineData("see docs.maintainer-domain.invalid")]
    [InlineData("under /home/someone/work")]
    [InlineData(@"in C:\Users\someone")]
    [InlineData("write to someone@example.org")]
    [InlineData("key sk-abcdefghijklmnop1234")]
    public void TheDenylist_CatchesEachKind(string sample) => Assert.NotEmpty(Findings(sample));

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

    [GeneratedRegex(@"sk-[A-Za-z0-9_-]{16,}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretKey();
}
