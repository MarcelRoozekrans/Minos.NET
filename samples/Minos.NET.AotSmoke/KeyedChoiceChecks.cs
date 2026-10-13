namespace Minos.AotSmoke;

/// <summary><see cref="KeyedChoice"/>'s default constructor and equality, over an evaluated answer, under Native AOT.</summary>
internal static class KeyedChoiceChecks
{
    [Covers("Minos.KeyedChoice.KeyedChoice() -> void")]
    [Covers("Minos.KeyedChoice.Equals(Minos.KeyedChoice other) -> bool")]
    public static async Task KeyedChoiceIsCompared()
    {
        var evaluated = await SmokeAnswers.ProductAsync().ConfigureAwait(false);
        var rebuilt = evaluated;
        var empty = new KeyedChoice();

        Program.Check(
            rebuilt.Equals(evaluated)
                && string.Equals(rebuilt.Value, "pro-plan", StringComparison.Ordinal)
                && Math.Abs(rebuilt.Probabilities["pro-plan"] - 0.9) < 1e-12
                && !evaluated.Equals(empty)
                && empty.Equals(default)
                && empty.Value.Length == 0,
            "an evaluated KeyedChoice equals its copy, and the empty one has no value, under Native AOT");
    }
}
