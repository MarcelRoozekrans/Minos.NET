namespace Minos.AotSmoke;

/// <summary><see cref="KeyedChoice"/>'s default constructor and equality, over an evaluated answer, under Native AOT.</summary>
internal static class KeyedChoiceChecks
{
    [Covers("Minos.KeyedChoice.KeyedChoice() -> void")]
    [Covers("Minos.KeyedChoice.Equals(Minos.KeyedChoice other) -> bool")]
    public static async Task KeyedChoiceIsCompared()
    {
        var evaluated = await SmokeAnswers.ProductAsync().ConfigureAwait(false);
        var again = await SmokeAnswers.ProductAsync().ConfigureAwait(false);
        var other = await SmokeAnswers.OtherProductAsync().ConfigureAwait(false);
        var empty = new KeyedChoice();

        Program.Check(
            again.Equals(evaluated)
                && string.Equals(again.Value, "pro-plan", StringComparison.Ordinal)
                && Math.Abs(again.Probabilities["pro-plan"] - 0.9) < 1e-12
                && !evaluated.Equals(other)
                && !evaluated.Equals(empty)
                && empty.Equals(default)
                && empty.Value.Length == 0,
            "an evaluated KeyedChoice equals an independent one and differs from another, and the empty one has no value, under Native AOT");
    }
}
