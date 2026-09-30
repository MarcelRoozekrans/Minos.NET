using System.Text.Json.Nodes;
using ZeroAlloc.Jev.Transport;

namespace ZeroAlloc.Jev.Tests;

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
        using var body = TypedRequestWriter.WriteUtf8(UrgencyCheck.QuestionsUtf8, """{"a":1}"""u8, "m", new CountingPool());

        var sent = JsonNode.Parse(body.Span)!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(UrgencyCheck.QuestionsUtf8), sent["questions"]));
        Assert.Equal(1, (int)sent["state"]!["a"]!);
    }
}
