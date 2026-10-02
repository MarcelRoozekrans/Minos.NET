namespace ZeroAlloc.Jev.Samples.Reranking;

/// <summary>A customer question and the id of the article a person would pick as its answer.</summary>
public sealed record Query(string Text, string BestId);

/// <summary>Five authored customer questions, each worded to share more surface words with a weaker article.</summary>
public static class Queries
{
    public static IReadOnlyList<Query> All { get; } =
    [
        new("The lock won't close and the app still says I'm riding", "a04"),
        new("I got charged although the bike had a flat tyre", "a09"),
        new("How do I stop my membership before it renews next month", "a08"),
        new("My friend wants to ride along on my account", "a21"),
        new("Can I get a receipt for my company's expenses", "a16"),
    ];
}
