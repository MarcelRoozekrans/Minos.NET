using System.Collections.ObjectModel;
using System.Text.Json;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Telemetry;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Jev.Tests;

/// <summary><see cref="SystemOneResponse.Confidences"/>, the raw path's per-answer confidences.</summary>
public sealed class AnswerConfidencesTests
{
    [Theory]
    [InlineData("response-choice.json", new[] { 0.81 })]
    [InlineData("response-score.json", new[] { 0.92 })]
    public void Confidences_AreEachChoiceAndScoreAnswer(string fixture, double[] expected)
        => Assert.Equal(expected, Read(Response(fixture).Confidences));

    [Fact]
    public void Confidences_OfANoulAnswer_AreNone() => Assert.Empty(Read(Response("response-noul.json").Confidences));

    [Fact]
    public void Confidences_OverAnyDictionary_AreRead()
    {
        var response = Response("response-choice.json");
        var wrapped = new SystemOneResponse
        {
            Model = response.Model,
            Usage = response.Usage,
            Answers = new ReadOnlyDictionary<string, JevAnswer>(new Dictionary<string, JevAnswer>(response.Answers, StringComparer.Ordinal)),
        };

        Assert.Equal([0.81], Read(wrapped.Confidences));
    }

    [Fact]
    public void Confidences_OfADeserializedResponse_AllocateNothing()
    {
        var response = Response("response-choice.json");
        var sum = 0d;

        AllocationGate.AssertBudget(
            0,
            1000,
            () =>
            {
                foreach (var confidence in response.Confidences)
                {
                    sum += confidence;
                }
            },
            "AnswerConfidences");

        Assert.True(sum > 0);
    }

    private static SystemOneResponse Response(string fixture)
        => JsonSerializer.Deserialize(Fixture.Text(fixture), JevJsonContext.Default.SystemOneResponse)!;

    private static double[] Read(AnswerConfidences confidences)
    {
        var read = new List<double>();
        foreach (var confidence in confidences)
        {
            read.Add(confidence);
        }

        return [.. read];
    }
}
