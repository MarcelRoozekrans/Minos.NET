using System.Text.Json;
using Minos.Tests.WireFixtureSets;
using Minos.Protocols;

namespace Minos.Tests;

/// <summary>A generated set's question count, counted from its definition.</summary>
public sealed class GeneratedQuestionCountTests
{
    [Fact]
    public void OneQuestion() => AssertCount<UrgencyCheck>(1);

    [Fact]
    public void OneQuestionOfEachKind() => AssertCount<TicketTriage>(3);

    [Fact]
    public void StructuredCriteria() => AssertCount<StructuredRouting>(2);

    [Fact]
    public void Count_ComesFromTheDefinition()
        => Assert.Equal(3, GeneratedQuestionCount<WfMixed>.Value);

    private static void AssertCount<T>(int expected)
        where T : IQuestionSet<T>
    {
        Assert.Equal(expected, GeneratedQuestionCount<T>.Value);

        using var questions = JsonDocument.Parse(SystemOneProtocol.QuestionsJson(T.Definition).ToArray());
        Assert.Equal(expected, questions.RootElement.GetPropertyCount());
    }
}
