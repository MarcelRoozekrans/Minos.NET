namespace Minos.AotSmoke;

/// <summary>
/// <see cref="IQuestionSet{TSelf}.Create"/> called through the interface's static abstract member under Native AOT, the
/// way generic library code calls it.
/// </summary>
internal static class IQuestionSetChecks
{
    /// <summary>
    /// Proves only that a call through the type parameter dispatches to the generated <c>Create</c>: empty slots stop it
    /// at the first accessor's index guard. Its success path, reading real slots, is not run here; it now runs under
    /// Native AOT through the typed evaluation checks, which read answers through the generated <c>Create</c>.
    /// </summary>
    [Covers("Minos.IQuestionSet<TSelf>.Create(Minos.AnswerSlots answers) -> TSelf")]
    public static void CreateRunsThroughTheInterface()
    {
        bool refused;
        try
        {
            CreateFrom<SmokeTriage>(default);
            refused = false;
        }
        catch (ArgumentOutOfRangeException)
        {
            refused = true;
        }

        Program.Check(
            refused,
            "IQuestionSet<TSelf>.Create, called through a type parameter, reaches the generated Create, which refuses empty slots, under Native AOT");
    }

    private static T CreateFrom<T>(AnswerSlots answers)
        where T : IQuestionSet<T>
        => T.Create(answers);
}
