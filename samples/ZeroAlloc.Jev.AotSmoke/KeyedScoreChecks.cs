namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><see cref="KeyedScore"/>'s constructors and equality, over an evaluated answer, under Native AOT.</summary>
internal static class KeyedScoreChecks
{
    [Covers("ZeroAlloc.Jev.KeyedScore.KeyedScore() -> void")]
    [Covers("ZeroAlloc.Jev.KeyedScore.KeyedScore(int level, double expected, double confidence, ZeroAlloc.Jev.KeyedProbabilityMap probabilities) -> void")]
    [Covers("ZeroAlloc.Jev.KeyedScore.Equals(ZeroAlloc.Jev.KeyedScore other) -> bool")]
    public static async Task KeyedScoreIsRebuiltAndCompared()
    {
        var evaluated = await SmokeAnswers.RiskAsync().ConfigureAwait(false);
        var rebuilt = new KeyedScore(evaluated.Level, evaluated.Expected, evaluated.Confidence, evaluated.Probabilities);
        var lower = new KeyedScore(evaluated.Level - 1, evaluated.Expected, evaluated.Confidence, evaluated.Probabilities);
        var empty = new KeyedScore();

        Program.Check(
            rebuilt.Equals(evaluated)
                && rebuilt.Level == 3
                && Math.Abs(rebuilt.Expected - 2.4) < 1e-12
                && !evaluated.Equals(lower)
                && !evaluated.Equals(empty)
                && empty.Equals(default),
            "a KeyedScore rebuilt from an evaluated one equals it, and a lower or empty one does not, under Native AOT");
    }
}
