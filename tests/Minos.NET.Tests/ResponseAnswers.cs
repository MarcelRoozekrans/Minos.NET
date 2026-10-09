using System.Text;
using System.Text.Json;

namespace Minos.Tests;

/// <summary>Positions a reader on a response's <c>answers</c> object and runs the generated parser.</summary>
internal static class ResponseAnswers
{
    public static T Parse<T>(string responseJson)
        where T : IQuestionSet<T>
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(responseJson));
        reader.Read();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isAnswers = reader.ValueTextEquals("answers"u8);
            reader.Read();
            if (isAnswers)
            {
                return T.Parse(ref reader);
            }

            reader.Skip();
        }

        throw new InvalidOperationException("The response has no 'answers' property.");
    }
}
