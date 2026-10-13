namespace Minos.AotSmoke;

/// <summary><see cref="KeyedScore"/>'s default constructor and equality, over an evaluated answer, under Native AOT.</summary>
internal static class KeyedScoreChecks
{
    [Covers("Minos.KeyedScore.KeyedScore() -> void")]
    [Covers("Minos.KeyedScore.Equals(Minos.KeyedScore other) -> bool")]
    public static async Task KeyedScoreIsCompared()
    {
        var evaluated = await SmokeAnswers.RiskAsync().ConfigureAwait(false);
        var rebuilt = evaluated;
        var empty = new KeyedScore();

        Program.Check(
            rebuilt.Equals(evaluated)
                && rebuilt.Level == 3
                && Math.Abs(rebuilt.Expected - 2.4) < 1e-12
                && !evaluated.Equals(empty)
                && empty.Equals(default),
            "an evaluated KeyedScore equals its copy, and the empty one does not, under Native AOT");
    }
}
