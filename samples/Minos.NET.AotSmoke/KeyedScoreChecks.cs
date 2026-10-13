namespace Minos.AotSmoke;

/// <summary><see cref="KeyedScore"/>'s default constructor and equality, over an evaluated answer, under Native AOT.</summary>
internal static class KeyedScoreChecks
{
    [Covers("Minos.KeyedScore.KeyedScore() -> void")]
    [Covers("Minos.KeyedScore.Equals(Minos.KeyedScore other) -> bool")]
    public static async Task KeyedScoreIsCompared()
    {
        var evaluated = await SmokeAnswers.RiskAsync().ConfigureAwait(false);
        var again = await SmokeAnswers.RiskAsync().ConfigureAwait(false);
        var other = await SmokeAnswers.OtherRiskAsync().ConfigureAwait(false);
        var empty = new KeyedScore();

        Program.Check(
            again.Equals(evaluated)
                && again.Level == 3
                && Math.Abs(again.Expected - 2.4) < 1e-12
                && !evaluated.Equals(other)
                && !evaluated.Equals(empty)
                && empty.Equals(default),
            "an evaluated KeyedScore equals an independent one, and a different or the empty one does not, under Native AOT");
    }
}
