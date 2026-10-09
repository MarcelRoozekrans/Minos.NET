namespace Minos.AotSmoke;

/// <summary><see cref="KeyedChoice"/>'s constructors and equality, over an evaluated answer, under Native AOT.</summary>
internal static class KeyedChoiceChecks
{
    [Covers("Minos.KeyedChoice.KeyedChoice() -> void")]
    [Covers("Minos.KeyedChoice.KeyedChoice(string! value, double confidence, Minos.KeyedProbabilityMap probabilities) -> void")]
    [Covers("Minos.KeyedChoice.Equals(Minos.KeyedChoice other) -> bool")]
    public static async Task KeyedChoiceIsRebuiltAndCompared()
    {
        var evaluated = await SmokeAnswers.ProductAsync().ConfigureAwait(false);
        var rebuilt = new KeyedChoice(evaluated.Value, evaluated.Confidence, evaluated.Probabilities);
        var empty = new KeyedChoice();

        Program.Check(
            rebuilt.Equals(evaluated)
                && string.Equals(rebuilt.Value, "pro-plan", StringComparison.Ordinal)
                && Math.Abs(rebuilt.Probabilities["pro-plan"] - 0.9) < 1e-12
                && !evaluated.Equals(empty)
                && empty.Equals(default)
                && empty.Value.Length == 0,
            "a KeyedChoice rebuilt from an evaluated one equals it, and the empty one has no value, under Native AOT");
    }
}
