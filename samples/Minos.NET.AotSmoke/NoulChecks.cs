namespace Minos.AotSmoke;

/// <summary><see cref="Noul"/>'s parameterless constructor under Native AOT; its other members run in the allocation gates.</summary>
internal static class NoulChecks
{
    [Covers("Minos.Noul.Noul() -> void")]
    public static async Task EmptyNoulIsFalse()
    {
        var empty = new Noul();
        var triage = await SmokeAnswers.TriageAsync().ConfigureAwait(false);

        Program.Check(
            !empty.Value
                && empty.Probability == 0.0
                && empty.Equals(new Noul(0.0))
                && !empty.Equals(triage.RequestsCredentials),
            "the empty Noul is a false answer with probability 0 under Native AOT");
    }
}
