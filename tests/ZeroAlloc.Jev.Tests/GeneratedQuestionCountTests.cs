using System.Text.Json;

namespace ZeroAlloc.Jev.Tests;

/// <summary>A generated set's question count, read from the questions JSON the generator wrote.</summary>
public sealed class GeneratedQuestionCountTests
{
    [Fact]
    public void OneQuestion() => AssertCount<UrgencyCheck>(1);

    [Fact]
    public void OneQuestionOfEachKind() => AssertCount<TicketTriage>(3);

    [Fact]
    public void StructuredCriteria() => AssertCount<StructuredRouting>(2);

    [Fact]
    public void TruncatedQuestions_CountZero_AndDoNotThrow() => Assert.Equal(0, GeneratedQuestionCount<TruncatedSet>.Value);

    [Fact]
    public void QuestionsThatAreNotJson_CountZero_AndDoNotThrow() => Assert.Equal(0, GeneratedQuestionCount<NotJsonSet>.Value);

    [Fact]
    public void QuestionsThatAreNotAnObject_CountZero() => Assert.Equal(0, GeneratedQuestionCount<ArraySet>.Value);

    private static void AssertCount<T>(int expected)
        where T : IJevQuestionSet<T>
    {
        Assert.Equal(expected, GeneratedQuestionCount<T>.Value);

        using var questions = JsonDocument.Parse(T.QuestionsUtf8.ToArray());
        Assert.Equal(expected, questions.RootElement.GetPropertyCount());
    }

    // Hand-written sets with questions the generator would never write.
    private sealed class TruncatedSet : IJevQuestionSet<TruncatedSet>
    {
        public static ReadOnlySpan<byte> QuestionsUtf8 => "{\"a\":{\"kind\":\"noul\"},\"b\":{\"kind\":"u8;

        public static TruncatedSet Parse(ref Utf8JsonReader answers) => throw new NotSupportedException();
    }

    private sealed class NotJsonSet : IJevQuestionSet<NotJsonSet>
    {
        public static ReadOnlySpan<byte> QuestionsUtf8 => "{not json"u8;

        public static NotJsonSet Parse(ref Utf8JsonReader answers) => throw new NotSupportedException();
    }

    private sealed class ArraySet : IJevQuestionSet<ArraySet>
    {
        public static ReadOnlySpan<byte> QuestionsUtf8 => "[1,2]"u8;

        public static ArraySet Parse(ref Utf8JsonReader answers) => throw new NotSupportedException();
    }
}
