using System.Buffers;
using System.Text;
using System.Text.Json;
using Minos.Generator;
using Minos.Serialization;

namespace Minos.Tests;

/// <summary>The runtime encoder escapes exactly as the generator's JsonText.AppendJsonString does.</summary>
public sealed class GeneratorJsonEncoderTests
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

    private static string Generator(string text) => new StringBuilder().AppendJsonString(text).ToString();

    private static string Write(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = GeneratorJsonEncoder.Instance }))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
