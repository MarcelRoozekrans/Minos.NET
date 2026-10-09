namespace Minos.Docs.Tests;

/// <summary>
/// Asserts that a sequence has exactly one item and returns it. The test projects' analyzers reject LINQ's and xUnit's
/// <c>Single</c> by name, so this uses <c>Assert.Collection</c>.
/// </summary>
internal static class Only
{
    public static T Of<T>(IEnumerable<T> items)
    {
        var all = items.ToArray();
        Assert.Collection(all, _ => { });
        return all[0];
    }
}
