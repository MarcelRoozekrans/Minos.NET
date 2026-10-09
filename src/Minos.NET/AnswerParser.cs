using System.Text.Json;

namespace Minos;

/// <summary>Reads a question set's typed answers from the <c>answers</c> object of a <c>/v1/systemone</c> response.</summary>
/// <typeparam name="TResult">The typed answers.</typeparam>
/// <param name="answers">A reader over complete JSON, positioned on the start of the <c>answers</c> object. It is left on the object's end.</param>
/// <returns>The typed answers.</returns>
/// <exception cref="JsonException">An answer is missing, has the wrong type, names an unknown option or level, or lacks a required field.</exception>
internal delegate TResult AnswerParser<TResult>(ref Utf8JsonReader answers);

/// <summary>The one <see cref="AnswerParser{TResult}"/> over a <c>[Questions]</c> set's static <c>Parse</c>.</summary>
/// <typeparam name="T">The question set.</typeparam>
internal static class GeneratedAnswerParser<T>
    where T : IQuestionSet<T>
{
    /// <summary>The parser, created once per set type.</summary>
    public static readonly AnswerParser<T> Instance = static (ref Utf8JsonReader answers) => T.Parse(ref answers);
}
