using System.Text;
using System.Text.Json;

namespace Minos.Tests;

/// <summary>The guards of the public <see cref="AnswerReader"/>, which ships until its removal; its other tests moved to SystemOneAnswersTests.</summary>
public sealed class AnswerReaderTests
{
    [Fact]
    public void ReadChoice_BufferTooSmall_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var reader = At("""{"type":"choice","choice":"red","probabilities":{},"confidence":0.5}""");
            AnswerReader.ReadChoice(ref reader, ColorOptions.Instance, new double[3], 1);
        });

    [Fact]
    public void ReadScoreCore_WithoutLevels_Throws()
        => Assert.Equal("options", Assert.Throws<ArgumentException>(() =>
        {
            var reader = At("""{"type":"score","score":0,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{},"confidence":1}""");
            AnswerReader.ReadScoreCore(ref reader, KeyedOptionSet.Levels(0), [], 0);
        }).ParamName);

    private static Utf8JsonReader At(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return reader;
    }
}
