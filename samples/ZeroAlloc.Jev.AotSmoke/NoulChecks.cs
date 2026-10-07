namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><see cref="Noul"/>'s parameterless constructor under Native AOT; its other members run in the allocation gates.</summary>
internal static class NoulChecks
{
    [Covers("ZeroAlloc.Jev.Noul.Noul() -> void")]
    public static void EmptyNoulIsFalse()
    {
        var empty = new Noul();

        Program.Check(
            !empty.Value
                && empty.Probability == 0.0
                && empty.Equals(new Noul(0.0))
                && !empty.Equals(SmokeAnswers.Triage().RequestsCredentials),
            "the empty Noul is a false answer with probability 0 under Native AOT");
    }
}
