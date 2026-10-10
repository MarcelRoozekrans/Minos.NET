using System.Text.Json;
using Minos.Protocols;
using Minos.Tests.WireFixtureSets;

namespace Minos.Tests;

/// <summary>A set with no questions, which the generator still implements.</summary>
[Questions]
public partial record NoQuestionsSet;

public sealed class GeneratedCreateTests
{
    private static ReadOnlySpan<byte> MixedAnswers => """
        {"requests_credentials":{"type":"noul","noul":0.2},
         "route_to":{"type":"choice","choice":"account","probabilities":{"billing":0.1,"account":0.8,"other":0.1},"confidence":0.7},
         "urgency":{"type":"score","score":1.4,"legend":{},"probabilities":{"0":0.1,"1":0.4,"2":0.5},"confidence":0.5}}
        """u8;

    [Fact]
    public void Create_ThroughTheProtocol_EqualsParse()
    {
        var parseReader = new Utf8JsonReader(MixedAnswers);
        parseReader.Read();
        var parsed = WfMixed.Parse(ref parseReader);

        var reader = new Utf8JsonReader(MixedAnswers);
        reader.Read();
        var created = SystemOneProtocol.Instance.ReadAnswers(ref reader, WfMixed.Definition, static answers => WfMixed.Create(answers));

        Assert.Equal(parsed.RequestsCredentials.Probability, created.RequestsCredentials.Probability);
        Assert.Equal(parsed.Team.Value, created.Team.Value);
        Assert.Equal(parsed.Team.Confidence, created.Team.Confidence);
        Assert.Equal(parsed.Urgency.Value, created.Urgency.Value);
        Assert.Equal(parsed.Urgency.Expected, created.Urgency.Expected);
    }

    [Fact]
    public void Create_ForASetWithNoQuestions_ReadsAnEmptyAnswersObject()
    {
        var reader = new Utf8JsonReader("{}"u8);
        reader.Read();
        var created = SystemOneProtocol.Instance.ReadAnswers(ref reader, NoQuestionsSet.Definition, static answers => NoQuestionsSet.Create(answers));

        Assert.Empty(NoQuestionsSet.Definition.Questions);
        Assert.Equal(new NoQuestionsSet(), created);
    }
}
