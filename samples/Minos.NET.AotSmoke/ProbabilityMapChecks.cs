namespace Minos.AotSmoke;

/// <summary><see cref="ProbabilityMap{T}"/>'s enumeration and equality, over a parsed answer, under Native AOT.</summary>
internal static class ProbabilityMapChecks
{
    [Covers("Minos.ProbabilityMap<T>.ProbabilityMap() -> void")]
    [Covers("Minos.ProbabilityMap<T>.GetEnumerator() -> Minos.ProbabilityMap<T>.Enumerator")]
    [Covers("Minos.ProbabilityMap<T>.Enumerator.Enumerator() -> void")]
    [Covers("Minos.ProbabilityMap<T>.Enumerator.MoveNext() -> bool")]
    [Covers("Minos.ProbabilityMap<T>.Equals(Minos.ProbabilityMap<T> other) -> bool")]
    public static async Task ProbabilityMapEnumeratesAndCompares()
    {
        var triage = await SmokeAnswers.TriageAsync().ConfigureAwait(false);
        var probabilities = triage.Team.Probabilities;
        var options = new List<Team>();
        var sum = 0.0;
        foreach (var (option, probability) in probabilities)
        {
            options.Add(option);
            sum += probability;
        }

        var empty = new ProbabilityMap<Team>();
        var emptyEnumerator = new ProbabilityMap<Team>.Enumerator();
        Program.Check(
            options.SequenceEqual([Team.Billing, Team.Account]) && Math.Abs(sum - 1.0) < 1e-12,
            "a parsed ProbabilityMap enumerates every option with its probability under Native AOT");
        Program.Check(
            probabilities.Equals(triage.Team.Probabilities)
                && !probabilities.Equals(empty)
                && empty.Equals(default)
                && empty.Count == 0
                && !empty.GetEnumerator().MoveNext()
                && !emptyEnumerator.MoveNext(),
            "ProbabilityMap equality compares values across buffers, and the empty map enumerates nothing, under Native AOT");
    }
}
