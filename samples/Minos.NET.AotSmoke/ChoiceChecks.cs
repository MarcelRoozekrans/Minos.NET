namespace Minos.AotSmoke;

/// <summary><see cref="Choice{T}"/>'s constructors and equality, over a parsed answer, under Native AOT.</summary>
internal static class ChoiceChecks
{
    [Covers("Minos.Choice<T>.Choice() -> void")]
    [Covers("Minos.Choice<T>.Choice(T value, double confidence, Minos.ProbabilityMap<T> probabilities) -> void")]
    [Covers("Minos.Choice<T>.Equals(Minos.Choice<T> other) -> bool")]
    public static void ChoiceIsRebuiltAndCompared()
    {
        var parsed = SmokeAnswers.Triage().Team;
        var rebuilt = new Choice<Team>(parsed.Value, parsed.Confidence, parsed.Probabilities);
        var empty = new Choice<Team>();

        Program.Check(
            rebuilt.Equals(parsed)
                && rebuilt.Value == Team.Account
                && Math.Abs(rebuilt.Probabilities[Team.Account] - 0.8) < 1e-12
                && !parsed.Equals(empty)
                && empty.Equals(default)
                && empty.Probabilities.Count == 0,
            "a Choice rebuilt from a parsed one equals it, and the empty Choice equals only itself, under Native AOT");
    }
}
