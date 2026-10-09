using System.Buffers;
using System.Text;
using System.Text.Json;
using Minos.Serialization;

namespace Minos.Tests;

/// <summary>Criterion writes exactly the attribute path's wire shapes.</summary>
public sealed class CriterionTests
{
    [Fact]
    public void Text_IsAJsonString() => Assert.Equal("\"Refunds\"", Wire(Criterion.Text("Refunds")));

    [Fact]
    public void String_ConvertsImplicitly() => Assert.Equal("\"Refunds\"", Wire("Refunds"));

    [Fact]
    public void ExamplesAndNotFor_AreACriterionObject()
        => Assert.Equal(
            """{"description":"Refunds","examples":["I was charged twice"],"not_for":["How much is Pro?"]}""",
            Wire(Criterion.Text("Refunds").WithExamples("I was charged twice").WithNotFor("How much is Pro?")));

    [Fact]
    public void NotForAlone_LeavesOutTheExamples()
        => Assert.Equal("""{"description":"Cosmetic","not_for":["Data loss"]}""", Wire(Criterion.Text("Cosmetic").WithNotFor("Data loss")));

    [Fact]
    public void EmptyLists_SendThePlainText() => Assert.Equal("\"a\"", Wire(Criterion.Text("a").WithExamples().WithNotFor()));

    [Fact]
    public void NullEntries_AreLeftOut()
    {
        Assert.Equal("""{"description":"a","examples":["x"]}""", Wire(Criterion.Text("a").WithExamples(null, "x")));
        Assert.Equal("\"a\"", Wire(Criterion.Text("a").WithExamples(null, null)));
    }

    [Fact]
    public void Json_IsTheJsonValue()
        => Assert.Equal(
            """{"description":"Login","owner":"identity"}""",
            Wire(Criterion.Json(DecisionContent.FromUtf8Json(""" { "description" : "Login", "owner" : "identity" } """u8))));

    [Fact]
    public void With_ReturnsANewCriterion()
    {
        var plain = Criterion.Text("a");
        _ = plain.WithExamples("x");

        Assert.Equal("\"a\"", Wire(plain));
    }

    [Fact]
    public void WithExamples_CopiesTheEntries()
    {
        var examples = new[] { "x" };
        var criterion = Criterion.Text("a").WithExamples(examples);
        examples[0] = "changed";

        Assert.Equal("""{"description":"a","examples":["x"]}""", Wire(criterion));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Equal("description", Assert.Throws<ArgumentNullException>(() => Criterion.Text(null!)).ParamName);
        Assert.Equal("description", Assert.Throws<ArgumentNullException>(() => (Criterion)(string)null!).ParamName);
        Assert.Equal("json", Assert.Throws<ArgumentException>(() => Criterion.Json(default)).ParamName);
        Assert.Equal("json", Assert.Throws<ArgumentException>(() => Criterion.Json("text")).ParamName);
        Assert.Throws<InvalidOperationException>(() => Criterion.Json(DecisionContent.FromUtf8Json("{}"u8)).WithExamples("x"));
        Assert.Throws<InvalidOperationException>(() => Criterion.Json(DecisionContent.FromUtf8Json("[]"u8)).WithNotFor("x"));
    }

    private static string Wire(Criterion criterion)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = GeneratorJsonEncoder.Instance }))
        {
            criterion.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
