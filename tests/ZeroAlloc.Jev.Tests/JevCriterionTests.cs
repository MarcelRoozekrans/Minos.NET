using System.Buffers;
using System.Text;
using System.Text.Json;
using ZeroAlloc.Jev.Serialization;

namespace ZeroAlloc.Jev.Tests;

/// <summary>JevCriterion writes exactly the attribute path's wire shapes.</summary>
public sealed class JevCriterionTests
{
    [Fact]
    public void Text_IsAJsonString() => Assert.Equal("\"Refunds\"", Wire(JevCriterion.Text("Refunds")));

    [Fact]
    public void String_ConvertsImplicitly() => Assert.Equal("\"Refunds\"", Wire("Refunds"));

    [Fact]
    public void ExamplesAndNotFor_AreACriterionObject()
        => Assert.Equal(
            """{"description":"Refunds","examples":["I was charged twice"],"not_for":["How much is Pro?"]}""",
            Wire(JevCriterion.Text("Refunds").WithExamples("I was charged twice").WithNotFor("How much is Pro?")));

    [Fact]
    public void NotForAlone_LeavesOutTheExamples()
        => Assert.Equal("""{"description":"Cosmetic","not_for":["Data loss"]}""", Wire(JevCriterion.Text("Cosmetic").WithNotFor("Data loss")));

    [Fact]
    public void EmptyLists_SendThePlainText() => Assert.Equal("\"a\"", Wire(JevCriterion.Text("a").WithExamples().WithNotFor()));

    [Fact]
    public void NullEntries_AreLeftOut()
    {
        Assert.Equal("""{"description":"a","examples":["x"]}""", Wire(JevCriterion.Text("a").WithExamples(null, "x")));
        Assert.Equal("\"a\"", Wire(JevCriterion.Text("a").WithExamples(null, null)));
    }

    [Fact]
    public void Json_IsTheJsonValue()
        => Assert.Equal(
            """{"description":"Login","owner":"identity"}""",
            Wire(JevCriterion.Json(JevContent.FromUtf8Json(""" { "description" : "Login", "owner" : "identity" } """u8))));

    [Fact]
    public void With_ReturnsANewCriterion()
    {
        var plain = JevCriterion.Text("a");
        _ = plain.WithExamples("x");

        Assert.Equal("\"a\"", Wire(plain));
    }

    [Fact]
    public void WithExamples_CopiesTheEntries()
    {
        var examples = new[] { "x" };
        var criterion = JevCriterion.Text("a").WithExamples(examples);
        examples[0] = "changed";

        Assert.Equal("""{"description":"a","examples":["x"]}""", Wire(criterion));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Equal("description", Assert.Throws<ArgumentNullException>(() => JevCriterion.Text(null!)).ParamName);
        Assert.Equal("description", Assert.Throws<ArgumentNullException>(() => (JevCriterion)(string)null!).ParamName);
        Assert.Equal("json", Assert.Throws<ArgumentException>(() => JevCriterion.Json(default)).ParamName);
        Assert.Equal("json", Assert.Throws<ArgumentException>(() => JevCriterion.Json("text")).ParamName);
        Assert.Throws<InvalidOperationException>(() => JevCriterion.Json(JevContent.FromUtf8Json("{}"u8)).WithExamples("x"));
        Assert.Throws<InvalidOperationException>(() => JevCriterion.Json(JevContent.FromUtf8Json("[]"u8)).WithNotFor("x"));
    }

    private static string Wire(JevCriterion criterion)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = GeneratorJsonEncoder.Instance }))
        {
            criterion.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
