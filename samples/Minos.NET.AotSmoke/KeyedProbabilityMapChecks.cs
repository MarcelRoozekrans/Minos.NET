namespace Minos.AotSmoke;

/// <summary><see cref="KeyedProbabilityMap"/>'s enumeration and equality, over an evaluated answer, under Native AOT.</summary>
internal static class KeyedProbabilityMapChecks
{
    [Covers("Minos.KeyedProbabilityMap.KeyedProbabilityMap() -> void")]
    [Covers("Minos.KeyedProbabilityMap.GetEnumerator() -> Minos.KeyedProbabilityMap.Enumerator")]
    [Covers("Minos.KeyedProbabilityMap.Enumerator.Enumerator() -> void")]
    [Covers("Minos.KeyedProbabilityMap.Enumerator.MoveNext() -> bool")]
    [Covers("Minos.KeyedProbabilityMap.Equals(Minos.KeyedProbabilityMap other) -> bool")]
    public static async Task KeyedProbabilityMapEnumeratesAndCompares()
    {
        var probabilities = (await SmokeAnswers.RiskAsync().ConfigureAwait(false)).Probabilities;
        var other = (await SmokeAnswers.RiskAsync().ConfigureAwait(false)).Probabilities;
        var keys = new List<string>();
        var sum = 0.0;
        foreach (var (key, probability) in probabilities)
        {
            keys.Add(key);
            sum += probability;
        }

        var empty = new KeyedProbabilityMap();
        var emptyEnumerator = new KeyedProbabilityMap.Enumerator();
        Program.Check(
            keys.Count == 4 && Math.Abs(sum - 1.0) < 1e-12 && Math.Abs(probabilities[3] - 0.5) < 1e-12,
            "an evaluated KeyedProbabilityMap enumerates every level with its probability under Native AOT");
        Program.Check(
            probabilities.Equals(other)
                && !probabilities.Equals(empty)
                && empty.Equals(default)
                && empty.Count == 0
                && !empty.GetEnumerator().MoveNext()
                && !emptyEnumerator.MoveNext(),
            "KeyedProbabilityMap equality compares keys and values, and the empty map enumerates nothing, under Native AOT");
    }
}
