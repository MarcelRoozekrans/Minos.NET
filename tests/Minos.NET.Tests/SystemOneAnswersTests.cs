using System.Text;
using System.Text.Json;
using Minos.Protocols;

namespace Minos.Tests;

public sealed class SystemOneAnswersTests
{
    private static readonly byte[][] ColorKeys = Utf8Keys.Encode(["red", "green", "blue"]);
    private static readonly byte[][] UrgencyKeys = Utf8Keys.Encode(["0", "1", "2"]);

    // Hoisted per CA1861 (prefer a static readonly field over a constant array argument).
    private static readonly double[] ExpectedBuffer = [0.0, 0.0, 0.1, 0.7, 0.2];

    [Fact]
    public void ReadNoul_ReadsProbability_AndStopsOnEndObject()
    {
        var reader = At("""{"type":"noul","noul":0.95}""");

        var noul = SystemOneAnswers.ReadNoul(ref reader);

        Assert.Equal(0.95, noul);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
        Assert.Equal(0, reader.CurrentDepth);
    }

    [Fact]
    public void ReadNoul_TypeLast_AndUnknownFieldsSkipped()
    {
        var reader = At("""{"extra":{"a":[1,{"b":2}]},"noul":0.4,"type":"noul"}""");

        var noul = SystemOneAnswers.ReadNoul(ref reader);

        Assert.Equal(0.4, noul);
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
            SystemOneAnswers.ReadNoul(ref reader);
        });

    [Fact]
    public void ReadChoice_ReadsAnswer_IntoItsBufferSlice()
    {
        var buffer = new double[5];
        var reader = At("""{"type":"choice","choice":"green","probabilities":{"red":0.1,"green":0.7,"blue":0.2},"confidence":0.81}""");

        var (choice, confidence) = SystemOneAnswers.ReadChoice(ref reader, ColorKeys, buffer, 2);

        Assert.Equal(Color.Green, ColorOptions.Instance[choice]);
        Assert.Equal(0.81, confidence);
        Assert.Equal(0.7, buffer[2 + choice]);
        Assert.Equal(ExpectedBuffer, buffer);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
    }

    [Fact]
    public void ReadChoice_ProbabilitiesBeforeChoice_Resolves()
    {
        var buffer = new double[5];
        var reader = At("""{"type":"choice","probabilities":{"red":0.1,"green":0.7,"blue":0.2},"choice":"green","confidence":0.81}""");

        var (choice, confidence) = SystemOneAnswers.ReadChoice(ref reader, ColorKeys, buffer, 2);

        Assert.Equal(Color.Green, ColorOptions.Instance[choice]);
        Assert.Equal(0.81, confidence);
        Assert.Equal(0.7, buffer[2 + choice]);
        Assert.Equal(ExpectedBuffer, buffer);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
    }

    [Fact]
    public void ReadChoice_EscapedOptionKey_Resolves()
    {
        var reader = At("""{"type":"choice","choice":"gr\u0065en","probabilities":{},"confidence":0.5}""");

        var buffer = new double[3];
        var (choice, _) = SystemOneAnswers.ReadChoice(ref reader, ColorKeys, buffer, 0);

        Assert.Equal(Color.Green, ColorOptions.Instance[choice]);
        Assert.Equal(0.0, buffer[0]);
    }

    [Theory]
    [InlineData("""{"type":"choice","choice":"purple","probabilities":{},"confidence":0.5}""")]
    [InlineData("""{"type":"choice","choice":"red","probabilities":{"purple":1.0},"confidence":0.5}""")]
    [InlineData("""{"type":"choice","choice":"red","confidence":0.5}""")]
    [InlineData("""{"type":"choice","choice":"red","probabilities":{}}""")]
    [InlineData("""{"type":"score","choice":"red","legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{},"confidence":0.5}""")]
    public void ReadChoice_Invalid_Throws(string json)
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = At(json);
            SystemOneAnswers.ReadChoice(ref reader, ColorKeys, new double[3], 0);
        });

    [Fact]
    public void ReadScore_ValueIsArgmax_ExpectedIsWireScore()
    {
        var reader = At("""{"type":"score","score":1.05,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.95,"2":0.05},"confidence":0.92}""");

        var buffer = new double[3];
        var (level, expected, confidence) = SystemOneAnswers.ReadScore(ref reader, UrgencyKeys, buffer, 0);

        Assert.Equal(Urgency.Medium, UrgencyLevels.Instance[level]);
        Assert.Equal(1.05, expected);
        Assert.Equal(0.92, confidence);
        Assert.Equal(0.05, buffer[2]);
    }

    [Fact]
    public void ReadScore_Tie_PicksLowerLevel()
    {
        var reader = At("""{"type":"score","score":1.0,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.45,"1":0.1,"2":0.45},"confidence":0.3}""");

        var (level, _, _) = SystemOneAnswers.ReadScore(ref reader, UrgencyKeys, new double[3], 0);

        Assert.Equal(Urgency.Low, UrgencyLevels.Instance[level]);
    }

    [Fact]
    public void ReadScore_WithoutLegend_Throws()
    {
        var ex = Assert.Throws<JsonException>(() =>
        {
            var reader = At("""{"type":"score","score":1.0,"probabilities":{"0":0.1,"1":0.8,"2":0.1},"confidence":0.5}""");
            SystemOneAnswers.ReadScore(ref reader, UrgencyKeys, new double[3], 0);
        });

        Assert.Contains("'legend'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"x\"")]
    [InlineData("[]")]
    [InlineData("null")]
    public void ReadScore_NonObjectLegend_Throws(string legend)
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = At($$"""{"type":"score","score":1.0,"legend":{{legend}},"probabilities":{"0":0.1,"1":0.8,"2":0.1},"confidence":0.5}""");
            SystemOneAnswers.ReadScore(ref reader, UrgencyKeys, new double[3], 0);
        });

    [Theory]
    [InlineData("""{"type":"score","score":1.0,"legend":{"0":"Low"},"probabilities":{"3":1.0},"confidence":0.5}""")]
    [InlineData("""{"type":"score","legend":{"0":"Low"},"probabilities":{"0":1.0},"confidence":0.5}""")]
    [InlineData("""{"type":"noul","score":1.0,"legend":{"0":"Low"},"probabilities":{"0":1.0},"confidence":0.5}""")]
    public void ReadScore_Invalid_Throws(string json)
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = At(json);
            SystemOneAnswers.ReadScore(ref reader, UrgencyKeys, new double[3], 0);
        });

    [Fact]
    public void NextProperty_JsonEndsInsideObject_Throws()
        => Assert.ThrowsAny<JsonException>(() =>
        {
            var reader = new Utf8JsonReader("""{"a":1"""u8, isFinalBlock: false, state: default);
            reader.Read();
            reader.Read();
            reader.Read();
            SystemOneAnswers.NextProperty(ref reader);
        });

    [Fact]
    public void MissingAnswer_NamesTheKey()
        => Assert.Contains("'team'", SystemOneAnswers.MissingAnswer("team").Message, StringComparison.Ordinal);

    private static Utf8JsonReader At(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return reader;
    }
}
