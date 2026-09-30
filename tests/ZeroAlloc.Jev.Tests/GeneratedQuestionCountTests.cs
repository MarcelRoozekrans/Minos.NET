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
    public void JsonTextAtTheDepthLimit() => AssertCount<DeepJsonCheck>(2);

    private static void AssertCount<T>(int expected)
        where T : IJevQuestionSet<T>
    {
        Assert.Equal(expected, GeneratedQuestionCount<T>.Value);

        using var questions = JsonDocument.Parse(T.QuestionsUtf8.ToArray());
        Assert.Equal(expected, questions.RootElement.GetPropertyCount());
    }
}
