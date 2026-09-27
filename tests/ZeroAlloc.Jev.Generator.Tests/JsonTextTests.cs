using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.Generator.Tests;

public sealed class JsonTextTests
{
    [Theory]
    [InlineData("plain text")]
    [InlineData("quote \" and backslash \\")]
    [InlineData("line\nbreak\r\ttab")]
    [InlineData("control \u0001 and \u001f")]
    [InlineData("non-ASCII é ü ß")]
    [InlineData("emoji 😀")]
    [InlineData("backtick `message` and </script>")]
    public void AppendJsonString_RoundTrips_AsPlainAscii(string value)
    {
        var json = new StringBuilder().AppendJsonString(value).ToString();

        Assert.All(json, c => Assert.InRange(c, ' ', '~'));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(value, document.RootElement.GetString());
    }

    [Fact]
    public void CSharpLiteral_EscapesQuotesAndBackslashes()
        => Assert.Equal(@"""{\""a\"":\""\\n\""}""", JsonText.CSharpLiteral("{\"a\":\"\\n\"}"));

    [Fact]
    public void CSharpLiteral_EscapesControlCharacters()
        => Assert.Equal("\"a\\u000ab\"", JsonText.CSharpLiteral("a\nb"));

    [Fact]
    public void CSharpLiteral_EscapesLineSeparator()
        => Assert.Equal("\"a\\u2028b\"", JsonText.CSharpLiteral("a\u2028b"));

    [Fact]
    public void CSharpLiteral_EscapesParagraphSeparator()
        => Assert.Equal("\"a\\u2029b\"", JsonText.CSharpLiteral("a\u2029b"));

    [Fact]
    public void CSharpLiteral_EscapesNextLine()
        => Assert.Equal("\"a\\u0085b\"", JsonText.CSharpLiteral("a\u0085b"));
}
