namespace Minos.AotSmoke;

/// <summary><see cref="Score{T}"/>'s constructors and equality, over a parsed answer, under Native AOT.</summary>
internal static class ScoreChecks
{
    [Covers("Minos.Score<T>.Score() -> void")]
    [Covers("Minos.Score<T>.Score(T value, double expected, double confidence, Minos.ProbabilityMap<T> probabilities) -> void")]
    [Covers("Minos.Score<T>.Equals(Minos.Score<T> other) -> bool")]
    public static async Task ScoreIsRebuiltAndCompared()
    {
        var parsed = (await SmokeAnswers.TriageAsync().ConfigureAwait(false)).Urgency;
        var rebuilt = new Score<Urgency>(parsed.Value, parsed.Expected, parsed.Confidence, parsed.Probabilities);
        var shifted = new Score<Urgency>(parsed.Value, parsed.Expected - 0.5, parsed.Confidence, parsed.Probabilities);
        var empty = new Score<Urgency>();

        Program.Check(
            rebuilt.Equals(parsed)
                && rebuilt.Value == Urgency.High
                && Math.Abs(rebuilt.Expected - 1.9) < 1e-12
                && !parsed.Equals(shifted)
                && !parsed.Equals(empty)
                && empty.Equals(default)
                && empty.Normalized == 0.0,
            "a Score rebuilt from a parsed one equals it, and a shifted or empty one does not, under Native AOT");
    }
}
