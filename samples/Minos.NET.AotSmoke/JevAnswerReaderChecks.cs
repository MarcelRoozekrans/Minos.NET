using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary><see cref="JevAnswerReader.MissingAnswer"/> under Native AOT: the exception generated code throws for an absent answer.</summary>
internal static class JevAnswerReaderChecks
{
    [Covers("static Minos.JevAnswerReader.MissingAnswer(string! key) -> System.Text.Json.JsonException!")]
    public static void MissingAnswerNamesTheQuestion()
    {
        var exception = JevAnswerReader.MissingAnswer("team");

        Program.Check(
            exception.Message.Contains("'team'", StringComparison.Ordinal),
            "JevAnswerReader.MissingAnswer names the question with no answer under Native AOT");
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
