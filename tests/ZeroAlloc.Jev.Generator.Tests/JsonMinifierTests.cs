using System.Linq;

namespace ZeroAlloc.Jev.Generator.Tests;

public sealed class JsonMinifierTests
{
    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("[]", "[]")]
    [InlineData(" { \"a\" : 1 ,\n\t\"b\" : [ true , false , null ] } ", "{\"a\":1,\"b\":[true,false,null]}")]
    [InlineData("[-0, 0.5, 1e+5, 2E-3, 12, -1.25e10]", "[-0,0.5,1e+5,2E-3,12,-1.25e10]")]
    [InlineData("{\"q\":\"say \\\"hi\\\" \\\\ \\/ \\b\\f\\n\\r\\t\"}", "{\"q\":\"say \\\"hi\\\" \\\\ / \\u0008\\u000c\\n\\r\\t\"}")]
    [InlineData("[\"\\u00e9\", \"é\"]", "[\"\\u00e9\",\"\\u00e9\"]")]
    [InlineData("[\"\\ud83d\\ude00\", \"😀\"]", "[\"\\ud83d\\ude00\",\"\\ud83d\\ude00\"]")]
    [InlineData("{\"a\":{\"b\":{\"c\":[[[]]]}}}", "{\"a\":{\"b\":{\"c\":[[[]]]}}}")]
    [InlineData("{\"a\":1,\"a\":2}", "{\"a\":1,\"a\":2}")]
    public void Minify_Valid_ReturnsMinifiedAscii(string text, string expected)
    {
        var result = JsonMinifier.Minify(text);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(expected, result.Json);
        Assert.All(result.Json!, c => Assert.InRange(c, ' ', '~'));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 3)]
    [InlineData("\"text\"", 0)]
    [InlineData("42", 0)]
    [InlineData("true", 0)]
    [InlineData("null", 0)]
    [InlineData("{", 1)]
    [InlineData("[1,]", 3)]
    [InlineData("{\"a\":1,}", 7)]
    [InlineData("{'a':1}", 1)]
    [InlineData("{a:1}", 1)]
    [InlineData("[01]", 2)]
    [InlineData("[1.]", 3)]
    [InlineData("[.5]", 1)]
    [InlineData("[1e]", 3)]
    [InlineData("[+1]", 1)]
    [InlineData("[tru]", 1)]
    [InlineData("[\"a\nb\"]", 3)]
    [InlineData("[\"\\x\"]", 3)]
    [InlineData("[\"\\u12G4\"]", 6)]
    [InlineData("[\"abc]", 6)]
    [InlineData("[] []", 3)]
    [InlineData("[] // c", 3)]
    [InlineData("[/* c */]", 1)]
    [InlineData("\uFEFF[]", 0)]
    public void Minify_Invalid_ReportsOffset(string text, int offset)
    {
        var result = JsonMinifier.Minify(text);

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Equal(offset, result.ErrorOffset);
    }

    public static TheoryData<string, int> LoneSurrogates => new()
    {
        { "[\"\\ud800\"]", 2 },
        { "[\"\\udc00\"]", 2 },
        { "[\"\\ud800\\u0041\"]", 2 },
    };

    [Theory]
    [MemberData(nameof(LoneSurrogates))]
    public void Minify_LoneSurrogate_Fails(string text, int offset)
    {
        var result = JsonMinifier.Minify(text);

        Assert.False(result.Succeeded);
        Assert.Equal(offset, result.ErrorOffset);
    }

    // A raw (unescaped) lone surrogate cannot go through [MemberData]: xunit.runner.visualstudio serializes
    // Theory arguments for VSTest's test-case discovery, and that round trip replaces an unpaired surrogate with
    // U+FFFD before the test body runs, so two distinct raw-surrogate rows would collapse into one already-valid
    // string and never exercise JsonMinifier at all. A literal in the test body has no such round trip.
    [Fact]
    public void Minify_RawHighSurrogate_Fails()
        => AssertFailsAt("[\"" + (char)0xD800 + "\"]", 2);

    [Fact]
    public void Minify_RawLowSurrogate_Fails()
        => AssertFailsAt("[\"" + (char)0xDC00 + "\"]", 2);

    [Fact]
    public void Minify_EscapedHighSurrogateThenRawLowSurrogate_Fails()
        => AssertFailsAt("[\"\\" + "ud800" + (char)0xDC00 + "\"]", 2);

    [Fact]
    public void Minify_Invalid_ReturnsNoStrings()
    {
        var result = JsonMinifier.Minify("[\"first\",\"second\",]");

        Assert.False(result.Succeeded);
        Assert.Empty(result.Strings.ToArray());
    }

    [Fact]
    public void Minify_Depth60_Succeeds()
        => Assert.True(JsonMinifier.Minify(new string('[', 60) + new string(']', 60)).Succeeded);

    [Fact]
    public void Minify_Depth61_Fails()
        => AssertFailsAt(new string('[', 61) + new string(']', 61), 60);

    private static void AssertFailsAt(string text, int offset)
    {
        var result = JsonMinifier.Minify(text);

        Assert.False(result.Succeeded);
        Assert.Equal(offset, result.ErrorOffset);
    }

    [Fact]
    public void Minify_ReturnsStringValuesInOrder_NotKeys()
    {
        var result = JsonMinifier.Minify("{\"question\":\"Is `a` like `b`?\",\"data\":{\"k\":[\"x\\ny\",1]}}");

        Assert.Equal(["Is `a` like `b`?", "x\ny"], result.Strings.ToArray());
    }
}
