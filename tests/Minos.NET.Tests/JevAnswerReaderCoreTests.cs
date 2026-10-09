using System.Text;
using System.Text.Json;

namespace Minos.Tests;

/// <summary>The non-generic Choice and Score core that the generic readers and the keyed answers share.</summary>
public sealed class JevAnswerReaderCoreTests
{
    private static readonly KeyedOptionSet Products = new(["pro-plan", "team-plan", "other"]);

    [Fact]
    public void ReadChoiceCore_ReadsTheIndexConfidenceAndProbabilities_AtTheOffset()
    {
        var buffer = new double[5];
        var reader = Reader("""{"type":"choice","choice":"team-plan","probabilities":{"pro-plan":0.25,"team-plan":0.7},"confidence":0.66}""");

        var (choice, confidence) = JevAnswerReader.ReadChoiceCore(ref reader, Products, buffer, 2);

        Assert.Equal(1, choice);
        Assert.Equal(0.66, confidence);
        Assert.Equal([0.0, 0.0, 0.25, 0.7, 0.0], buffer);
        Assert.Equal(JsonTokenType.EndObject, reader.TokenType);
    }

    [Fact]
    public void ReadChoiceCore_UnknownKey_Throws()
        => Assert.Throws<JsonException>(() =>
        {
            var reader = Reader("""{"type":"choice","choice":"legacy-plan","probabilities":{},"confidence":0.5}""");
            JevAnswerReader.ReadChoiceCore(ref reader, Products, new double[3], 0);
        });

    [Fact]
    public void ReadScoreCore_ReadsTheArgmax_TheLowerLevelOnATie()
    {
        var buffer = new double[3];
        var reader = Reader("""{"type":"score","score":0.8,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.4,"1":0.4,"2":0.2},"confidence":0.5}""");

        var (level, expected, confidence) = JevAnswerReader.ReadScoreCore(ref reader, KeyedOptionSet.Levels(3), buffer, 0);

        Assert.Equal(0, level);
        Assert.Equal(0.8, expected);
        Assert.Equal(0.5, confidence);
    }

    [Fact]
    public void ReadScoreCore_WithoutLevels_Throws()
        => Assert.Equal("options", Assert.Throws<ArgumentException>(() =>
        {
            var reader = Reader("""{"type":"score","score":0,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{},"confidence":1}""");
            JevAnswerReader.ReadScoreCore(ref reader, KeyedOptionSet.Levels(0), [], 0);
        }).ParamName);

    [Fact]
    public void KeyedOptionSet_MapsKeysBothWays()
    {
        Assert.Equal(3, Products.Count);
        Assert.Equal("team-plan", Products[1]);
        Assert.Equal(2, Products.IndexOf("other"));
        Assert.Equal(-1, Products.IndexOf("Other"));
        var levels = KeyedOptionSet.Levels(3);
        Assert.Equal(["0", "1", "2"], new[] { levels[0], levels[1], levels[2] });
    }

    [Fact]
    public void KeyedOptionSet_IndexOfKey_ComparesTheUnescapedUtf8()
    {
        var options = new KeyedOptionSet(["café", "tea"]);
        var reader = Reader("\"caf\\u00e9\"");

        Assert.Equal(0, options.IndexOfKey(ref reader));
    }

    [Fact]
    public void KeyedOptionSet_Indexer_OutOfRange_Throws()
    {
        Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => Products[3]).ParamName);
        Assert.Equal("index", Assert.Throws<ArgumentOutOfRangeException>(() => Products[-1]).ParamName);
    }

    [Fact]
    public void KeyedOptionSet_Levels_NegativeCount_Throws()
        => Assert.Equal("count", Assert.Throws<ArgumentOutOfRangeException>(() => KeyedOptionSet.Levels(-1)).ParamName);

    private static Utf8JsonReader Reader(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return reader;
    }
}
