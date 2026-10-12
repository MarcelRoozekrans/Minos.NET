namespace Minos.Conformance.Tests;

public sealed class ProbeTests
{
    [Fact]
    public void TheProbes_RunInThisOrder_WithStableIds()
        => Assert.Equal(["noul", "choice", "score", "mixed", "keyed", "invalid", "bad-key"], ConformanceProbe.All.Select(probe => probe.Id));

    [Fact]
    public void EachProbe_AsksTheSameState_AndNoModel()
        => Assert.All(ConformanceProbe.All, probe =>
        {
            Assert.Equal(DecisionContent.FromString(ConformanceProbe.State), probe.Request.State);
            Assert.Null(probe.Request.Model);
        });

    [Fact]
    public void TheKinds_AreAnswerInvalidAndBadKey()
    {
        Assert.Equal(ProbeKind.Invalid, ConformanceProbe.Invalid.Kind);
        Assert.Empty(ConformanceProbe.Invalid.Definition.Questions);
        Assert.Equal(ProbeKind.BadKey, ConformanceProbe.BadKey.Kind);
        Assert.Equal(["is_urgent"], ConformanceProbe.BadKey.Definition.Questions.Select(question => question.Key));
        Assert.All(ConformanceProbe.All.Take(5), probe => Assert.Equal(ProbeKind.Answer, probe.Kind));
    }

    [Fact]
    public void TheMixedProbe_AsksItsKeysOutOfAlphabeticalOrder()
    {
        var keys = ConformanceProbe.Mixed.Definition.Questions.Select(question => question.Key).ToArray();

        Assert.Equal(["is_urgent", "department", "frustration"], keys);
        Assert.NotEqual(keys.Order(StringComparer.Ordinal), keys);
    }

    [Fact]
    public void TheKeyedProbe_IsABuiltSet_WithKeyedOptions()
    {
        var questions = ConformanceProbe.Keyed.Definition.Questions;

        Assert.Equal(["product", "effort"], questions.Select(question => question.Key));
        Assert.Equal(["pro-plan", "team-plan"], questions[0].Options.Select(option => option.Key));
        Assert.Equal(["0", "1", "2"], questions[1].Options.Select(option => option.Key));
    }

    [Fact]
    public void NoProbe_SetsWhatOpenAICannotSend()
        => Assert.All(
            ConformanceProbe.All.SelectMany(probe => probe.Definition.Questions),
            question =>
            {
                Assert.Null(question.WhenTrue);
                Assert.Null(question.WhenFalse);
                Assert.All(question.Options, option => Assert.Empty(option.Criterion?.Examples ?? []));
            });
}
