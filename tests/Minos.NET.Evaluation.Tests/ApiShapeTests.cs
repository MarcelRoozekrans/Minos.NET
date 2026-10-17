using System.Reflection;

namespace Minos.Evaluation.Tests;

// The shapes of the result and report types: no positional records, factories that keep a Noul answer consistent,
// and report types that only the library builds.
public sealed class ApiShapeTests
{
    private static readonly Type[] InputTypes = [typeof(RunHeader), typeof(ItemResult), typeof(AnswerRecord), typeof(ItemError), typeof(ModelPrice)];

    private static readonly Type[] OutputTypes =
        [typeof(KindMetrics), typeof(ReliabilityBin), typeof(SweepPoint), typeof(LatencySummary), typeof(CostSummary), typeof(ErrorSummary), typeof(ModelCount)];

    [Fact]
    public void NoResultOrReportTypeIsARecord()
    {
        Type[] all = [.. InputTypes, .. OutputTypes];
        foreach (var type in all)
        {
            Assert.True(type.IsSealed, type.Name + " is not sealed.");
            Assert.Null(type.GetMethod("<Clone>$"));
            Assert.Null(type.GetMethod("Deconstruct"));
        }
    }

    [Fact]
    public void ReportTypes_HaveNoPublicConstructor()
    {
        foreach (var type in OutputTypes)
        {
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }
    }

    [Fact]
    public void AnswerRecord_HasNoPublicConstructor()
        => Assert.Empty(typeof(AnswerRecord).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

    [Theory]
    [InlineData(0.3, false, 0.7)]
    [InlineData(0.5, true, 0.5)]
    [InlineData(0.93, true, 0.93)]
    [InlineData(0, false, 1)]
    [InlineData(1, true, 1)]
    public void ForNoul_DerivesTheAnswerAndConfidenceFromTheProbability(double p, bool noul, double confidence)
    {
        var answer = AnswerRecord.ForNoul(p);

        Assert.Equal(QuestionKind.Noul, answer.Kind);
        Assert.Equal(noul, answer.Noul);
        Assert.Equal(p, answer.Probability);
        Assert.Equal(Math.Max(p, 1 - p), answer.Confidence);
        Assert.Equal(confidence, answer.Confidence, 12);
        Assert.Null(answer.ChoiceKey);
        Assert.Equal(0, answer.ScoreLevel);
    }

    [Fact]
    public void ForChoice_KeepsTheKeyAndConfidence()
    {
        var answer = AnswerRecord.ForChoice("two", 0.4);

        Assert.Equal(QuestionKind.Choice, answer.Kind);
        Assert.Equal("two", answer.ChoiceKey);
        Assert.Equal(0.4, answer.Confidence);
        Assert.False(answer.Noul);
        Assert.Null(answer.Probability);
        Assert.Equal(0, answer.ScoreLevel);
    }

    [Fact]
    public void ForScore_KeepsTheLevelAndConfidence()
    {
        var answer = AnswerRecord.ForScore(2, 0.8);

        Assert.Equal(QuestionKind.Score, answer.Kind);
        Assert.Equal(2, answer.ScoreLevel);
        Assert.Equal(0.8, answer.Confidence);
        Assert.False(answer.Noul);
        Assert.Null(answer.Probability);
        Assert.Null(answer.ChoiceKey);
    }

    [Fact]
    public void Factories_RefuseValuesNoAnswerCanHave()
    {
        foreach (var bad in new[] { -0.1, 1.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AnswerRecord.ForNoul(bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnswerRecord.ForChoice("k", bad));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnswerRecord.ForScore(1, bad));
        }

        Assert.Throws<ArgumentNullException>(() => AnswerRecord.ForChoice(null!, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => AnswerRecord.ForScore(-1, 0.5));
    }

    [Fact]
    public void EvaluationResults_CopiesTheItems()
    {
        var items = new List<ItemResult> { ReportFixture.Answered(1, new Dictionary<string, AnswerRecord> { ["q"] = AnswerRecord.ForNoul(0.9) }) };
        var results = new EvaluationResults(ReportFixture.Results().Header, items);

        items.Add(ReportFixture.Failed(2));
        items[0] = ReportFixture.Failed(1);

        var only = Assert.Single(results.Items);
        Assert.NotNull(only.Answers);
        Assert.Throws<NotSupportedException>(() => ((IList<ItemResult>)results.Items).Add(only));
    }
}
