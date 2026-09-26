using System.Text.Json;

namespace Jev.Net;

/// <summary>A set of Jev questions declared as a C# type. The <c>[JevQuestions]</c> source generator implements it.</summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface IJevQuestionSet<TSelf>
    where TSelf : IJevQuestionSet<TSelf>
{
    /// <summary>Gets the <c>questions</c> object of a <c>/v1/systemone</c> request, as UTF-8 JSON.</summary>
    static abstract ReadOnlySpan<byte> QuestionsUtf8 { get; }

    /// <summary>Reads the typed answers from the <c>answers</c> object of a <c>/v1/systemone</c> response.</summary>
    /// <param name="answers">A reader over complete JSON, positioned on the start of the <c>answers</c> object. It is left on the object's end.</param>
    /// <returns>The typed answers.</returns>
    /// <exception cref="JsonException">An answer is missing, has the wrong type, names an unknown option or level, or lacks a required field.</exception>
    static abstract TSelf Parse(ref Utf8JsonReader answers);
}
