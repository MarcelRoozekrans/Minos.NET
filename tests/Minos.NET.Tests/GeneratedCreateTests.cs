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
    public void Create_ThroughTheProtocol_ReadsEveryAnswer()
    {
        var reader = new Utf8JsonReader(MixedAnswers);
        reader.Read();
        var created = SystemOneProtocol.Instance.ReadAnswers(ref reader, WfMixed.Definition, static answers => WfMixed.Create(answers));

        Assert.Equal(0.2, created.RequestsCredentials.Probability);

        Assert.Equal(WfTeam.Account, created.Team.Value);
        Assert.Equal(0.7, created.Team.Confidence);
        Assert.Equal(3, created.Team.Probabilities.Count);
        Assert.Equal(0.1, created.Team.Probabilities[WfTeam.Billing]);
        Assert.Equal(0.8, created.Team.Probabilities[WfTeam.Account]);
        Assert.Equal(0.1, created.Team.Probabilities[WfTeam.Other]);

        Assert.Equal(WfUrgency.High, created.Urgency.Value);
        Assert.Equal(1.4, created.Urgency.Expected);
        Assert.Equal(0.5, created.Urgency.Confidence);
        Assert.Equal(3, created.Urgency.Probabilities.Count);
        Assert.Equal(0.1, created.Urgency.Probabilities[WfUrgency.Low]);
        Assert.Equal(0.4, created.Urgency.Probabilities[WfUrgency.Medium]);
        Assert.Equal(0.5, created.Urgency.Probabilities[WfUrgency.High]);
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
