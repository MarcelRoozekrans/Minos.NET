namespace Minos.AotSmoke;

/// <summary><see cref="Choice{T}"/>'s default constructor and equality, over a parsed answer, under Native AOT.</summary>
internal static class ChoiceChecks
{
    [Covers("Minos.Choice<T>.Choice() -> void")]
    [Covers("Minos.Choice<T>.Equals(Minos.Choice<T> other) -> bool")]
    public static async Task ChoiceIsCompared()
    {
        var parsed = (await SmokeAnswers.TriageAsync().ConfigureAwait(false)).Team;
        var again = (await SmokeAnswers.TriageAsync().ConfigureAwait(false)).Team;
        var other = (await SmokeAnswers.OtherTriageAsync().ConfigureAwait(false)).Team;
        var empty = new Choice<Team>();

        Program.Check(
            again.Equals(parsed)
                && again.Value == Team.Account
                && Math.Abs(again.Probabilities[Team.Account] - 0.8) < 1e-12
                && !parsed.Equals(other)
                && !parsed.Equals(empty)
                && empty.Equals(default)
                && empty.Probabilities.Count == 0,
            "a parsed Choice equals an independent one, differs from another and from the empty one, and the empty Choice equals only itself, under Native AOT");
    }
}
