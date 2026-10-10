using System.Buffers;
using System.Text;
using System.Text.Json;
using Minos.Protocols;

namespace Minos.Tests;

/// <summary>The runtime encoder escapes exactly as the generator did, which <see cref="ReferenceJsonEscaping"/> keeps.</summary>
public sealed class SystemOneJsonEncoderTests
{
    public static TheoryData<string> Texts => new()
    {
        EdgeCases.TrickyInstructions,
        "<>&'+ del\u007f bs\b ff\f cr\r tab\t / slash",
        "zh 中文 bom \uFEFF",
        string.Empty,
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void StringValue_MatchesTheGenerator(string text) => Assert.Equal(Generator(text), Write(w => w.WriteStringValue(text)));

    [Theory]
    [MemberData(nameof(Texts))]
    public void PropertyName_MatchesTheGenerator(string text)
        => Assert.Equal("{" + Generator(text) + ":null}", Write(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName(text);
            w.WriteNullValue();
            w.WriteEndObject();
        }));

    [Theory]
    [MemberData(nameof(Texts))]
    public void Utf8StringValue_MatchesTheGenerator(string text)
        => Assert.Equal(Generator(text), Write(w => w.WriteStringValue(Encoding.UTF8.GetBytes(text))));

    [Theory]
    [MemberData(nameof(Texts))]
    public void Utf8PropertyName_MatchesTheGenerator(string text)
        => Assert.Equal("{" + Generator(text) + ":null}", Write(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName(Encoding.UTF8.GetBytes(text));
            w.WriteNullValue();
            w.WriteEndObject();
        }));

    [Fact]
    public void LoneSurrogates_MatchTheGenerator()
    {
        // Built here: xUnit's runner replaces lone surrogates in theory data with U+FFFD.
        var text = "lone " + (char)0xD800 + " end" + (char)0xDC00 + ((char)0x2028).ToString() + (char)0x2029;

        Assert.Equal(Generator(text), Write(w => w.WriteStringValue(text)));
    }

    [Fact]
    public void JsonElement_IsWrittenCompactWithTheGeneratorsEscaping()
    {
        using var document = JsonDocument.Parse(
            """ { "q" : "say \"hi\" \u00e9 é 😀 <b> \/" , "n": [1.50, -0, 1e+5, 2E-3], "a":1, "a":2 } """);

        Assert.Equal(
            """{"q":"say \"hi\" \u00e9 \u00e9 \ud83d\ude00 <b> /","n":[1.50,-0,1e+5,2E-3],"a":1,"a":2}""",
            Write(w => document.RootElement.WriteTo(w)));
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("quote \" and backslash \\")]
    [InlineData("line\nbreak\r\ttab")]
    [InlineData("control \u0001 and \u001f")]
    [InlineData("non-ASCII é ü ß")]
    [InlineData("emoji \U0001F600")]
    [InlineData("backtick `message` and </script>")]
    public void StringValue_RoundTrips_AsPlainAscii(string value)
    {
        var json = Write(w => w.WriteStringValue(value));

        Assert.All(json, c => Assert.InRange(c, ' ', '~'));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(value, document.RootElement.GetString());
    }

    // Each lone-surrogate case is built inside a [Fact] body: xUnit's runner replaces lone surrogates in theory data
    // with U+FFFD, so the input would already be valid and the test would pass without the escaping rule.
    private const char Replacement = (char)0xFFFD;

    [Fact]
    public void LoneHighSurrogateMidString_BecomesReplacementCharacter()
        => AssertReplaced("a" + (char)0xD800 + "b", "a" + Replacement + "b");

    [Fact]
    public void LoneLowSurrogateMidString_BecomesReplacementCharacter()
        => AssertReplaced("a" + (char)0xDC00 + "b", "a" + Replacement + "b");

    [Fact]
    public void LowThenHighSurrogate_BecomesTwoReplacementCharacters()
        => AssertReplaced("a" + (char)0xDC00 + (char)0xD800, "a" + Replacement + Replacement);

    [Fact]
    public void HighSurrogateAtEnd_BecomesReplacementCharacter()
        => AssertReplaced("end" + (char)0xD83D, "end" + Replacement);

    [Fact]
    public void TwoConsecutiveHighSurrogates_BecomeTwoReplacementCharacters()
        => AssertReplaced("a" + (char)0xD800 + (char)0xD801 + "b", "a" + Replacement + Replacement + "b");

    [Fact]
    public void SurrogatePair_IsKeptAsTwoEscapes()
        => Assert.Equal("\"\\ud83d\\ude00\"", Write(w => w.WriteStringValue("\U0001F600")));

    private static void AssertReplaced(string value, string expected)
    {
        Assert.Contains(value, char.IsSurrogate);

        var json = Write(w => w.WriteStringValue(value));

        Assert.Equal(Generator(value), json);
        Assert.All(json, c => Assert.InRange(c, ' ', '~'));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, document.RootElement.GetString());
    }

    private static string Generator(string text) => new StringBuilder().AppendJsonString(text).ToString();

    private static string Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = SystemOneJsonEncoder.Instance }))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
