using System.Text;
using System.Text.Json;
using Minos.Protocols;

namespace Minos.Tests;

/// <summary>Positions a reader on a response's <c>answers</c> object and reads them through the protocol into the generated type.</summary>
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
                return SystemOneProtocol.Instance.ReadAnswers(ref reader, T.Definition, static answers => T.Create(answers));
            }

            reader.Skip();
        }

        throw new InvalidOperationException("The response has no 'answers' property.");
    }
}
