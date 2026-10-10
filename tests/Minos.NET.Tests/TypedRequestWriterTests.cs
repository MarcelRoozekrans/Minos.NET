using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.Transport;
using Minos.Protocols;

namespace Minos.Tests;

/// <summary>The request writer copies whatever questions it is given, so a set built at run time can use it too.</summary>
public sealed class TypedRequestWriterTests
{
    [Fact]
    public void Write_CopiesTheGivenQuestions()
    {
        var pool = new CountingPool();

        using (var body = TypedRequestWriter.Write("""{"q":{"type":"noul","instructions":"x"}}"""u8, "text", "m", pool))
        {
            var sent = JsonNode.Parse(body.Span)!;
            Assert.Equal("""{"q":{"type":"noul","instructions":"x"}}""", sent["questions"]!.ToJsonString());
            Assert.Equal("text", (string?)sent["state"]);
            Assert.Equal("m", (string?)sent["model"]);
        }

        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void WriteUtf8_CopiesTheGivenQuestions()
    {
        using var body = TypedRequestWriter.WriteUtf8(SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition), """{"a":1}"""u8, "m", new CountingPool());

        var sent = JsonNode.Parse(body.Span)!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition)), sent["questions"]));
        Assert.Equal(1, (int)sent["state"]!["a"]!);
    }

    [Fact]
    public void WriteJsonElement_CopiesTheGivenQuestions()
    {
        var pool = new CountingPool();
        using var state = JsonDocument.Parse("""{"a":1}""");

        using (var body = TypedRequestWriter.Write(SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition), state.RootElement, "m", pool))
        {
            var sent = JsonNode.Parse(body.Span)!;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition)), sent["questions"]));
            Assert.Equal(1, (int)sent["state"]!["a"]!);
            Assert.Equal("m", (string?)sent["model"]);
        }

        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void WriteTypedState_CopiesTheGivenQuestions()
    {
        var pool = new CountingPool();

        using (var body = TypedRequestWriter.Write(
            SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition), new ContentSample("Ada", 36), ContentJsonContext.Default.ContentSample, "m", pool))
        {
            var sent = JsonNode.Parse(body.Span)!;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition)), sent["questions"]));
            Assert.Equal("Ada", (string?)sent["state"]!["name"]);
            Assert.Equal(36, (int)sent["state"]!["age"]!);
            Assert.Equal("m", (string?)sent["model"]);
        }

        Assert.Equal(0, pool.Outstanding);
    }
}
