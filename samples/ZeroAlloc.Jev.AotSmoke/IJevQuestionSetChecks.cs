using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// <see cref="IJevQuestionSet{TSelf}.Parse"/> called through the interface's static abstract member under Native AOT,
/// as the library's own typed evaluation calls it.
/// </summary>
internal static class IJevQuestionSetChecks
{
    [Covers("ZeroAlloc.Jev.IJevQuestionSet<TSelf>.Parse(ref System.Text.Json.Utf8JsonReader answers) -> TSelf")]
    public static void ParseRunsThroughTheInterface()
    {
        var triage = ParseThroughInterface<SmokeTriage>(Encoding.UTF8.GetBytes(Program.TriageAnswers));

        Program.Check(
            !triage.RequestsCredentials.Value && triage.Team.Value == Team.Account && triage.Urgency.Value == Urgency.High,
            "IJevQuestionSet<TSelf>.Parse, called through a type parameter, reads typed answers under Native AOT");
    }

    private static T ParseThroughInterface<T>(byte[] answers)
        where T : IJevQuestionSet<T>
    {
        var reader = new Utf8JsonReader(answers);
        reader.Read();
        return T.Parse(ref reader);
    }
}
