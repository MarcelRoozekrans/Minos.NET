namespace ZeroAlloc.Jev.Docs.Tests;

/// <summary>Every published page ends with a Next section, so a newcomer always has a step to take.</summary>
public sealed class NextSectionTests
{
    /// <summary>Pages that may end without a Next section, each with the reason. None is exempt today.</summary>
    private static readonly Dictionary<string, string> Terminal = new(StringComparer.Ordinal);

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();
        foreach (var page in PublishedPages.All)
        {
            if (!Terminal.ContainsKey(page))
            {
                data.Add(page);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryPage_EndsWithANextSectionThatLinks(string page)
    {
        var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, page));
        var heading = Array.FindLastIndex(lines, l => string.Equals(l, "## Next", StringComparison.Ordinal));

        Assert.True(heading >= 0, $"{page} has a '## Next' section.");
        var after = lines[(heading + 1)..];
        Assert.DoesNotContain(after, l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.NotEmpty(Markdown.Links(after));
    }
}
