using System.Text.Json;

namespace Minos;

/// <summary>
/// The number of questions in a <c>[JevQuestions]</c> set, for the client's logs and its
/// <c>jev.request.question_count</c> span tag: the top-level properties of its
/// <see cref="IJevQuestionSet{TSelf}.QuestionsUtf8"/>, the questions object the generator wrote at compile time.
/// </summary>
/// <remarks>
/// Read once per set type, on the type's first typed evaluation. It is not a generated constant: the set is compiled
/// into the caller's assembly, where an internal member is out of this library's reach, and a public one would be a
/// new member of <see cref="IJevQuestionSet{TSelf}"/>. The bytes the Minos.NET generator writes are always a valid
/// JSON object. A hand-written set may not be: when the top-level object cannot be read to its end, whether the bytes
/// are truncated or are not JSON, the count is 0 and the static initializer does not throw, so the request proceeds
/// as it did before the count was always read.
/// </remarks>
/// <typeparam name="T">The question set.</typeparam>
internal static class GeneratedQuestionCount<T>
    where T : IJevQuestionSet<T>
{
    /// <summary>The set's question count, or 0 when its questions are not a complete JSON object.</summary>
    public static readonly int Value = Count(T.QuestionsUtf8);

    private static int Count(ReadOnlySpan<byte> questionsUtf8)
    {
        // The generator caps JSON text at 60 levels below a question's instructions or criterion; 128 leaves room above
        // the root, question and criteria levels without depending on that arithmetic.
        // The reader is told the block is not final, so truncated input makes Read and TrySkip return false instead of
        // throwing. Bytes that are not JSON at all still throw a JsonException, the one failure left; it is caught by
        // type, so the well-formed path allocates nothing.
        var reader = new Utf8JsonReader(questionsUtf8, isFinalBlock: false, new JsonReaderState(new JsonReaderOptions { MaxDepth = 128 }));
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return 0;
            }

            var count = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return count;
                }

                count++;
                if (!reader.Read() || !reader.TrySkip())
                {
                    return 0;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON, so there is no question object to count.
        }

        return 0;
    }
}
