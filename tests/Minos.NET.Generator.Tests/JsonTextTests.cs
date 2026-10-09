using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Minos.Generator.Tests;

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

    // Each lone-surrogate case is built inside a [Fact] body. Through [Theory] data, xunit.runner.visualstudio
    // serializes the arguments for VSTest discovery and that round trip replaces an unpaired surrogate with U+FFFD
    // before the test runs, so the input would already be valid and the test would pass without the escaping rule.
    private const char Replacement = (char)0xFFFD;

    [Fact]
    public void AppendJsonString_LoneHighSurrogateMidString_BecomesReplacementCharacter()
        => AssertReplaced("a" + (char)0xD800 + "b", "a" + Replacement + "b");

    [Fact]
    public void AppendJsonString_LoneLowSurrogateMidString_BecomesReplacementCharacter()
        => AssertReplaced("a" + (char)0xDC00 + "b", "a" + Replacement + "b");

    [Fact]
    public void AppendJsonString_LowThenHighSurrogate_BecomesTwoReplacementCharacters()
        => AssertReplaced("a" + (char)0xDC00 + (char)0xD800, "a" + Replacement + Replacement);

    [Fact]
    public void AppendJsonString_HighSurrogateAtEnd_BecomesReplacementCharacter()
        => AssertReplaced("end" + (char)0xD83D, "end" + Replacement);

    [Fact]
    public void AppendJsonString_TwoConsecutiveHighSurrogates_BecomeTwoReplacementCharacters()
        => AssertReplaced("a" + (char)0xD800 + (char)0xD801 + "b", "a" + Replacement + Replacement + "b");

    private static void AssertReplaced(string value, string expected)
    {
        Assert.Contains(value, char.IsSurrogate);

        var json = new StringBuilder().AppendJsonString(value).ToString();

        Assert.All(json, c => Assert.InRange(c, ' ', '~'));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, document.RootElement.GetString());
    }

    [Fact]
    public void AppendJsonString_SurrogatePair_IsKept()
    {
        var json = new StringBuilder().AppendJsonString("\U0001F600").ToString();

        Assert.Equal("\"\\ud83d\\ude00\"", json);
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

    [Fact]
    public void CSharpLiteral_NonAscii_RoundTrips()
    {
        var value = "caf" + (char)0x00E9 + " " + char.ConvertFromUtf32(0x1F600);
        var expected = "\"" + string.Concat(value.Select(Escape)) + "\"";

        var literal = JsonText.CSharpLiteral(value);

        Assert.Equal(expected, literal);
        Assert.All(literal, c => Assert.InRange(c, ' ', '~'));

        var expression = SyntaxFactory.ParseExpression(literal);
        Assert.Empty(expression.GetDiagnostics());
        var token = Assert.IsType<LiteralExpressionSyntax>(expression).Token;
        Assert.Equal(value, token.ValueText);

        static string Escape(char c)
            => c is '"' or '\\'
                ? "\\" + c
                : c < ' ' || c > '~'
                    ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture)
                    : c.ToString();
    }
}
