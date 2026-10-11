namespace Minos.AotSmoke.Tests;

/// <summary>
/// A <c>*REMOVED*</c> line in <c>PublicAPI.Unshipped.txt</c> removes a member that <c>PublicAPI.Shipped.txt</c> holds. A
/// line with no shipped counterpart makes <c>tools/release/ship_release_tracking.py</c> exit at the next release, so it
/// is caught here, for every package.
/// </summary>
public sealed class RemovedLineTests
{
    private const string RemovedPrefix = "*REMOVED*";

    public static TheoryData<string> Packages => [.. PublicApi.Packages.Select(files => files[0])];

    [Theory]
    [MemberData(nameof(Packages))]
    public void EveryRemovedLineHasAShippedLine(string shippedFile)
    {
        var unshippedFile = shippedFile.Replace("Shipped", "Unshipped", StringComparison.Ordinal);
        var orphans = Orphans(ReadLines(shippedFile), ReadLines(unshippedFile));
        Assert.True(orphans.Count == 0, $"{unshippedFile} removes members that {shippedFile} does not hold:{Environment.NewLine}{string.Join(Environment.NewLine, orphans)}");
    }

    [Fact]
    public void AnOrphanedRemovedLineIsFound()
    {
        var orphans = Orphans(["A.B -> int"], ["*REMOVED*A.B -> int", "*REMOVED*A.C -> int"]);
        Assert.Equal(["*REMOVED*A.C -> int"], orphans);
    }

    private static List<string> Orphans(IEnumerable<string> shipped, IEnumerable<string> unshipped)
    {
        var held = shipped.ToHashSet(StringComparer.Ordinal);
        return [.. unshipped.Where(line => line.StartsWith(RemovedPrefix, StringComparison.Ordinal)
            && !held.Contains(line[RemovedPrefix.Length..]))];
    }

    private static List<string> ReadLines(string file) =>
    [
        .. File.ReadAllLines(Path.Combine(Repository.Root, file))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#')),
    ];
}
