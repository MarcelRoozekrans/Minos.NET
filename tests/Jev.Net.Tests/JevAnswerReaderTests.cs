using System.Text;
using System.Text.Json;

namespace Jev.Net.Tests;

public sealed class JevAnswerReaderTests
{
    // Hoisted per CA1861 (prefer a static readonly field over a constant array argument).
    private static readonly double[] ExpectedBuffer = [0.0, 0.0, 0.1, 0.7, 0.2];

    [Fact]
    public void ReadNoul_ReadsProbability_AndStopsOnEndObject()
    {
        var reader = At("""{"type":"noul","noul":0.95}""");

        var noul = JevAnswerReader.ReadNoul(ref reader);

        Assert.Equal(0.95, noul.Probability);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
        Assert.Equal(0, reader.CurrentDepth);
    }

    [Fact]
    public void ReadNoul_TypeLast_AndUnknownFieldsSkipped()
    {
        var reader = At("""{"extra":{"a":[1,{"b":2}]},"noul":0.4,"type":"noul"}""");

        var noul = JevAnswerReader.ReadNoul(ref reader);

        Assert.Equal(0.4, noul.Probability);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
    }

    [Theory]
    [InlineData("""{"type":"choice","noul":0.4}""")]
    [InlineData("""{"noul":0.4}""")]
    [InlineData("""{"type":"noul"}""")]
    [InlineData("""{"type":"noul","noul":"high"}""")]
    [InlineData("""[0.4]""")]
    public void ReadNoul_Invalid_Throws(string json)
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = At(json);
            JevAnswerReader.ReadNoul(ref reader);
        });

    [Fact]
    public void ReadChoice_ReadsAnswer_IntoItsBufferSlice()
    {
        var buffer = new double[5];
        var reader = At("""{"type":"choice","choice":"green","probabilities":{"red":0.1,"green":0.7,"blue":0.2},"confidence":0.81}""");

        var choice = JevAnswerReader.ReadChoice(ref reader, ColorOptions.Instance, buffer, 2);

        Assert.Equal(Color.Green, choice.Value);
        Assert.Equal(0.81, choice.Confidence);
        Assert.Equal(0.7, choice.Probabilities[Color.Green]);
        Assert.Equal(ExpectedBuffer, buffer);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
    }

    [Fact]
    public void ReadChoice_EscapedOptionKey_Resolves()
    {
        var reader = At("""{"type":"choice","choice":"gr\u0065en","probabilities":{},"confidence":0.5}""");

        var choice = JevAnswerReader.ReadChoice(ref reader, ColorOptions.Instance, new double[3], 0);

        Assert.Equal(Color.Green, choice.Value);
        Assert.Equal(0.0, choice.Probabilities[Color.Red]);
    }

    [Theory]
    [InlineData("""{"type":"choice","choice":"purple","probabilities":{},"confidence":0.5}""")]
    [InlineData("""{"type":"choice","choice":"red","probabilities":{"purple":1.0},"confidence":0.5}""")]
    [InlineData("""{"type":"choice","choice":"red","confidence":0.5}""")]
    [InlineData("""{"type":"choice","choice":"red","probabilities":{}}""")]
    [InlineData("""{"type":"score","choice":"red","probabilities":{},"confidence":0.5}""")]
    public void ReadChoice_Invalid_Throws(string json)
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = At(json);
            JevAnswerReader.ReadChoice(ref reader, ColorOptions.Instance, new double[3], 0);
        });

    [Fact]
    public void ReadChoice_BufferTooSmall_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var reader = At("""{"type":"choice","choice":"red","probabilities":{},"confidence":0.5}""");
            JevAnswerReader.ReadChoice(ref reader, ColorOptions.Instance, new double[3], 1);
        });

    [Fact]
    public void ReadScore_ValueIsArgmax_ExpectedIsWireScore()
    {
        var reader = At("""{"type":"score","score":1.05,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.95,"2":0.05},"confidence":0.92}""");

        var score = JevAnswerReader.ReadScore(ref reader, UrgencyLevels.Instance, new double[3], 0);

        Assert.Equal(Urgency.Medium, score.Value);
        Assert.Equal(1.05, score.Expected);
        Assert.Equal(0.92, score.Confidence);
        Assert.Equal(0.05, score.Probabilities[Urgency.High]);
    }

    [Fact]
    public void ReadScore_Tie_PicksLowerLevel()
    {
        var reader = At("""{"type":"score","score":1.0,"probabilities":{"0":0.45,"1":0.1,"2":0.45},"confidence":0.3}""");

        var score = JevAnswerReader.ReadScore(ref reader, UrgencyLevels.Instance, new double[3], 0);

        Assert.Equal(Urgency.Low, score.Value);
    }

    [Theory]
    [InlineData("""{"type":"score","score":1.0,"probabilities":{"3":1.0},"confidence":0.5}""")]
    [InlineData("""{"type":"score","probabilities":{"0":1.0},"confidence":0.5}""")]
    [InlineData("""{"type":"noul","score":1.0,"probabilities":{"0":1.0},"confidence":0.5}""")]
    public void ReadScore_Invalid_Throws(string json)
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = At(json);
            JevAnswerReader.ReadScore(ref reader, UrgencyLevels.Instance, new double[3], 0);
        });

    [Fact]
    public void NextProperty_JsonEndsInsideObject_Throws()
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = new Utf8JsonReader("""{"a":1"""u8, isFinalBlock: false, state: default);
            reader.Read();
            reader.Read();
            reader.Read();
            JevAnswerReader.NextProperty(ref reader);
        });

    [Fact]
    public void MissingAnswer_NamesTheKey()
        => Assert.Contains("'team'", JevAnswerReader.MissingAnswer("team").Message, StringComparison.Ordinal);

    private static Utf8JsonReader At(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return reader;
    }
}
