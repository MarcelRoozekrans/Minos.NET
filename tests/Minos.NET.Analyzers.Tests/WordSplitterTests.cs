extern alias CodeFixes;

using CodeFixes::Minos.CodeFixes;

namespace Minos.Analyzers.Tests;

/// <summary><see cref="WordSplitter"/>: an enum member name as the sentence-case text of its description.</summary>
public sealed class WordSplitterTests
{
    [Theory]
    [InlineData("Billing", "Billing")]
    [InlineData("NeedsAttention", "Needs attention")]
    [InlineData("HTTPError", "HTTP error")]
    [InlineData("ParseHTTPResponse", "Parse HTTP response")]
    [InlineData("HTTP", "HTTP")]
    [InlineData("HTTP2Error", "HTTP2 error")]
    [InlineData("X509Cert", "X509 cert")]
    [InlineData("Q4Results", "Q4 results")]
    [InlineData("Version2Beta", "Version2 beta")]
    [InlineData("Level10", "Level10")]
    [InlineData("Needs_Attention", "Needs attention")]
    [InlineData("NEEDS_ATTENTION", "Needs attention")]
    [InlineData("needs_attention", "needs attention")]
    [InlineData("_Leading", "Leading")]
    [InlineData("IsAValue", "Is a value")]
    [InlineData("A", "A")]
    public void ToSentence_SplitsTheName(string identifier, string expected)
        => Assert.Equal(expected, WordSplitter.ToSentence(identifier));
}
