namespace Minos.AotSmoke;

/// <summary><see cref="Score{T}"/>'s default constructor and equality, over a parsed answer, under Native AOT.</summary>
internal static class ScoreChecks
{
    [Covers("Minos.Score<T>.Score() -> void")]
    [Covers("Minos.Score<T>.Equals(Minos.Score<T> other) -> bool")]
    public static async Task ScoreIsCompared()
    {
        var parsed = (await SmokeAnswers.TriageAsync().ConfigureAwait(false)).Urgency;
        var rebuilt = parsed;
        var empty = new Score<Urgency>();

        Program.Check(
            rebuilt.Equals(parsed)
                && rebuilt.Value == Urgency.High
                && Math.Abs(rebuilt.Expected - 1.9) < 1e-12
                && !parsed.Equals(empty)
                && empty.Equals(default)
                && empty.Normalized == 0.0,
            "a parsed Score equals its copy, and the empty one does not, under Native AOT");
    }
}
