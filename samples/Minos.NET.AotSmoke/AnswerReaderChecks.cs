using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary><see cref="AnswerReader.MissingAnswer"/> under Native AOT: the exception generated code throws for an absent answer.</summary>
internal static class AnswerReaderChecks
{
    [Covers("static Minos.AnswerReader.MissingAnswer(string! key) -> System.Text.Json.JsonException!")]
    public static void MissingAnswerNamesTheQuestion()
    {
        var exception = AnswerReader.MissingAnswer("team");

        Program.Check(
            exception.Message.Contains("'team'", StringComparison.Ordinal),
            "AnswerReader.MissingAnswer names the question with no answer under Native AOT");
        Program.Check(
            SmokeAssert.Throws<JsonException>(() =>
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes("""{"requests_credentials":{"type":"noul","noul":0.1}}"""));
                reader.Read();
                _ = SmokeTriage.Parse(ref reader);
            }),
            "the generated Parse throws a JsonException when an answer is missing under Native AOT");
    }
}
