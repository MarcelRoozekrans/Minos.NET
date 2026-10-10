using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Minos.Generator.Tests;

public sealed class CSharpTextTests
{
    [Fact]
    public void CSharpLiteral_EscapesQuotesAndBackslashes()
        => Assert.Equal(@"""{\""a\"":\""\\n\""}""", CSharpText.CSharpLiteral("{\"a\":\"\\n\"}"));

    [Fact]
    public void CSharpLiteral_EscapesControlCharacters()
        => Assert.Equal("\"a\\u000ab\"", CSharpText.CSharpLiteral("a\nb"));

    [Fact]
    public void CSharpLiteral_EscapesLineSeparator()
        => Assert.Equal("\"a\\u2028b\"", CSharpText.CSharpLiteral("a\u2028b"));

    [Fact]
    public void CSharpLiteral_EscapesParagraphSeparator()
        => Assert.Equal("\"a\\u2029b\"", CSharpText.CSharpLiteral("a\u2029b"));

    [Fact]
    public void CSharpLiteral_EscapesNextLine()
        => Assert.Equal("\"a\\u0085b\"", CSharpText.CSharpLiteral("a\u0085b"));

    [Fact]
    public void CSharpLiteral_NonAscii_RoundTrips()
    {
        var value = "caf" + (char)0x00E9 + " " + char.ConvertFromUtf32(0x1F600);
        var expected = "\"" + string.Concat(value.Select(Escape)) + "\"";

        var literal = CSharpText.CSharpLiteral(value);

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
