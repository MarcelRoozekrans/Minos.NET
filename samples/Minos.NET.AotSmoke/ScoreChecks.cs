namespace Minos.AotSmoke;

/// <summary><see cref="Score{T}"/>'s default constructor and equality, over a parsed answer, under Native AOT.</summary>
internal static class ScoreChecks
{
    [Covers("Minos.Score<T>.Score() -> void")]
    [Covers("Minos.Score<T>.Equals(Minos.Score<T> other) -> bool")]
    public static async Task ScoreIsCompared()
    {
        var parsed = (await SmokeAnswers.TriageAsync().ConfigureAwait(false)).Urgency;
        var again = (await SmokeAnswers.TriageAsync().ConfigureAwait(false)).Urgency;
        var other = (await SmokeAnswers.OtherTriageAsync().ConfigureAwait(false)).Urgency;
        var empty = new Score<Urgency>();

        Program.Check(
            again.Equals(parsed)
                && again.Value == Urgency.High
                && Math.Abs(again.Expected - 1.9) < 1e-12
                && !parsed.Equals(other)
                && !parsed.Equals(empty)
                && empty.Equals(default)
                && empty.Normalized == 0.0,
            "a parsed Score equals an independent one, and a different or the empty one does not, under Native AOT");
    }
}
