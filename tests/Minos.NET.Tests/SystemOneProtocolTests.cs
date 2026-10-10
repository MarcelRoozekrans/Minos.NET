using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Minos.Protocols;

namespace Minos.Tests;

public sealed class SystemOneProtocolTests
{
    private static readonly QuestionSetDefinition Definition = new(
        QuestionDefinition.Noul("n", "N?"),
        QuestionDefinition.Choice("c", "C?", new OptionDefinition("a", "A"), new OptionDefinition("b", "B")),
        QuestionDefinition.Score("s", "S?", "Low", "High"));

    [Fact]
    public void QuestionsJson_IsCachedOnTheDefinition()
    {
        var first = SystemOneProtocol.QuestionsJson(Definition);
        var second = SystemOneProtocol.QuestionsJson(Definition);

        Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(first), ref MemoryMarshal.GetReference(second)));
    }

    [Fact]
    public void ReadAnswers_FillsSlotsInDefinitionOrder()
    {
        const string json = """
            {"s":{"type":"score","score":0.8,"legend":{},"probabilities":{"0":0.2,"1":0.8},"confidence":0.6},
             "unknown":{"type":"noul","noul":1},
             "c":{"type":"choice","choice":"b","probabilities":{"a":0.3,"b":0.7},"confidence":0.4},
             "n":{"type":"noul","noul":0.9}}
            """;
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();

        var result = SystemOneProtocol.Instance.ReadAnswers(ref reader, Definition, static answers =>
            (answers.Count, answers.Slots[0].Value, answers.Slots[1].ValueIndex, answers.Slots[2].ValueIndex, answers.Slots[2].Value));

        Assert.Equal((3, 0.9, 1, 1, 0.8), result);
    }

    [Fact]
    public void ReadAnswers_MissingAnswer_ThrowsTheSameMessage()
    {
        var exception = Assert.Throws<JsonException>(() =>
        {
            var reader = new Utf8JsonReader("""{"n":{"type":"noul","noul":0.9}}"""u8);
            reader.Read();
            SystemOneProtocol.Instance.ReadAnswers(ref reader, Definition, static answers => answers.Count);
        });
        Assert.Equal("The response has no answer for 'c'.", exception.Message);
    }

    [Fact]
    public void ReadAnswers_UnknownOption_ThrowsTheSameMessage()
    {
        var exception = Assert.Throws<JsonException>(() =>
        {
            var reader = new Utf8JsonReader("""{"n":{"type":"noul","noul":0.9},"c":{"type":"choice","choice":"z","probabilities":{},"confidence":1},"s":{}}"""u8);
            reader.Read();
            SystemOneProtocol.Instance.ReadAnswers(ref reader, Definition, static answers => answers.Count);
        });
        Assert.Equal("'z' is not one of the options.", exception.Message);
    }
}
