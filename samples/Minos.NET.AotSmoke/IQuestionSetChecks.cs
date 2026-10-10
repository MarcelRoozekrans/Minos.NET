using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="IQuestionSet{TSelf}.Parse"/> and <see cref="IQuestionSet{TSelf}.Create"/> called through the interface's
/// static abstract members under Native AOT, the way generic library code calls them.
/// </summary>
internal static class IQuestionSetChecks
{
    [Covers("Minos.IQuestionSet<TSelf>.Parse(ref System.Text.Json.Utf8JsonReader answers) -> TSelf")]
    public static void ParseRunsThroughTheInterface()
    {
        var triage = ParseThroughInterface<SmokeTriage>(Encoding.UTF8.GetBytes(Program.TriageAnswers));

        Program.Check(
            !triage.RequestsCredentials.Value && triage.Team.Value == Team.Account && triage.Urgency.Value == Urgency.High,
            "IQuestionSet<TSelf>.Parse, called through a type parameter, reads typed answers under Native AOT");
    }

    /// <summary>
    /// Proves only that a call through the type parameter dispatches to the generated <c>Create</c>: empty slots stop it
    /// at the first accessor's index guard. Its success path, reading real slots, is not run here; it runs under
    /// Native AOT through the typed evaluation checks once typed evaluation reads answers through <c>Create</c> rather
    /// than <c>Parse</c>.
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

    private static T ParseThroughInterface<T>(byte[] answers)
        where T : IQuestionSet<T>
    {
        var reader = new Utf8JsonReader(answers);
        reader.Read();
        return T.Parse(ref reader);
    }
}
