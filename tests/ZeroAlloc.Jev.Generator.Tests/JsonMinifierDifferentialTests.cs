using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZeroAlloc.Jev.Generator.Tests;

/// <summary>
/// JsonMinifier accepts exactly what System.Text.Json accepts, restricted to an object or array top level, with one
/// intended difference: duplicate object keys are accepted, as RFC 8259 allows and JsonDocument does, although JsonObject
/// rejects them. The corpus holds no duplicate keys.
/// </summary>
public sealed class JsonMinifierDifferentialTests
{
    public static TheoryData<string> Corpus => new()
    {
        "{}", "[]", "[[]]", "{\"a\":{}}", "[1,2,3]", "[-0]", "[0e0]", "[1E+2]", "[1e-2]", "[0.0001]",
        "[123456789012345678901234567890]", "[\"\"]", "[\"\\u0000\"]", "[\"\\/\"]", "{\"\":1}",
        " [ ] ", "\n{\n}\n", "[true,false,null]", "[\"é😀\"]", "[\"\\ud83d\\ude00\"]",
        "", " ", "[", "]", "{", "}", "[1,]", "[,1]", "{\"a\"}", "{\"a\":}", "{:1}", "{\"a\" 1}", "[1 2]",
        "[01]", "[-]", "[--1]", "[1.]", "[.1]", "[1e]", "[1e+]", "[0x1]", "[NaN]", "[Infinity]", "[+1]",
        "[True]", "[nul]", "[\"\\a\"]", "[\"\\u123\"]", "[\"\t\"]", "[\"\\ud800\"]", "[\"\\udc00\"]",
        "[] x", "[][]", "{'a':1}", "[// c\n]",
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Minify_AgreesWithSystemTextJson(string text)
    {
        var expected = SystemTextJsonParse(text);
        var result = JsonMinifier.Minify(text);

        var stjAccepts = expected is JsonObject or JsonArray;
        Assert.Equal(stjAccepts, result.Succeeded);
        if (stjAccepts)
        {
            Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(result.Json!)));
        }
    }

    private static JsonNode? SystemTextJsonParse(string text)
    {
        try
        {
            // JsonNode.Parse alone is lazy: it does not decode string values, so a syntactically well-formed but
            // ill-formed escape, such as an unpaired \ud800 surrogate, parses without error and only throws once
            // something reads the string. ToJsonString forces every value to be read, so the result here matches
            // System.Text.Json's real acceptance rather than just its initial parse.
            var node = JsonNode.Parse(text);
            _ = node?.ToJsonString();
            return node;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
