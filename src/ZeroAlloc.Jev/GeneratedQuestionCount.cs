using System.Text.Json;

namespace ZeroAlloc.Jev;

/// <summary>
/// The number of questions in a <c>[JevQuestions]</c> set, for the client's logs and its <c>jev.request.question_count</c> span tag: the top-level properties of its
/// <see cref="IJevQuestionSet{TSelf}.QuestionsUtf8"/>, the questions object the generator wrote at compile time.
/// </summary>
/// <remarks>
/// Read once per set type, on the type's first typed evaluation. It is not a generated constant: the
/// set is compiled into the caller's assembly, where an internal member is out of this library's reach, and a public
/// one would be a new member of <see cref="IJevQuestionSet{TSelf}"/>. The bytes are written by the ZeroAlloc.Jev
/// generator and are always a valid JSON object, so the static initializer does not throw; the first read runs it,
/// once per type.
/// </remarks>
/// <typeparam name="T">The question set.</typeparam>
internal static class GeneratedQuestionCount<T>
    where T : IJevQuestionSet<T>
{
    /// <summary>The set's question count.</summary>
    public static readonly int Value = Count(T.QuestionsUtf8);

    private static int Count(ReadOnlySpan<byte> questionsUtf8)
    {
        // The generator caps JSON text at 60 levels below a question's instructions or criterion; 128 leaves room above
        // the root, question and criteria levels without depending on that arithmetic.
        var reader = new Utf8JsonReader(questionsUtf8, new JsonReaderOptions { MaxDepth = 128 });
        reader.Read();
        var count = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            count++;
            reader.Read();
            reader.Skip();
        }

        return count;
    }
}
