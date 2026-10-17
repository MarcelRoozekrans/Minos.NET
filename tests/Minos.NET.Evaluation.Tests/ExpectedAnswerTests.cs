namespace Minos.Evaluation.Tests;

public sealed class ExpectedAnswerTests
{
    public static TheoryData<ExpectedAnswer, ExpectedAnswer> EqualPairs() => new()
    {
        { ExpectedAnswer.ForNoul(true), ExpectedAnswer.ForNoul(true) },
        { ExpectedAnswer.ForNoul(false), ExpectedAnswer.ForNoul(false) },
        { ExpectedAnswer.ForChoice("a"), ExpectedAnswer.ForChoice(new string('a', 1)) },
        { ExpectedAnswer.ForScore(1), ExpectedAnswer.ForScore(1) },
        { default, new ExpectedAnswer() },
        { default, ExpectedAnswer.ForNoul(false) },
    };

    public static TheoryData<ExpectedAnswer, ExpectedAnswer> DifferentPairs() => new()
    {
        { ExpectedAnswer.ForNoul(true), ExpectedAnswer.ForNoul(false) },
        { ExpectedAnswer.ForChoice("a"), ExpectedAnswer.ForChoice("b") },
        { ExpectedAnswer.ForScore(0), ExpectedAnswer.ForScore(1) },
        { ExpectedAnswer.ForNoul(false), ExpectedAnswer.ForScore(0) },
        { ExpectedAnswer.ForNoul(false), ExpectedAnswer.ForChoice("a") },
        { ExpectedAnswer.ForChoice("a"), ExpectedAnswer.ForScore(0) },
    };

    [Theory]
    [MemberData(nameof(EqualPairs))]
    public void EqualValues_AreEqualInEveryForm(ExpectedAnswer left, ExpectedAnswer right)
    {
        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object)right));
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Theory]
    [MemberData(nameof(DifferentPairs))]
    public void DifferentValues_AreNotEqualInAnyForm(ExpectedAnswer left, ExpectedAnswer right)
    {
        Assert.False(left.Equals(right));
        Assert.False(right.Equals(left));
        Assert.False(left.Equals((object)right));
        Assert.False(left == right);
        Assert.True(left != right);
    }

    [Fact]
    public void EqualsObject_IsFalseForNullAndForOtherTypes()
    {
        var answer = ExpectedAnswer.ForChoice("a");

        Assert.False(answer.Equals(null));
        Assert.False(answer.Equals("a"));
        Assert.False(answer.Equals(1));
    }

    [Fact]
    public void TheDefaultValue_IsAFalseNoul()
    {
        var answer = default(ExpectedAnswer);

        Assert.Equal(ExpectedAnswer.ForNoul(false).Kind, answer.Kind);
        Assert.False(answer.Noul);
        Assert.Null(answer.ChoiceKey);
        Assert.Equal(0, answer.ScoreLevel);
    }

    [Theory]
    [InlineData(true, "Noul true")]
    [InlineData(false, "Noul false")]
    public void ToString_NamesANoul(bool value, string expected)
        => Assert.Equal(expected, ExpectedAnswer.ForNoul(value).ToString());

    [Fact]
    public void ToString_NamesAChoiceAndAScore()
    {
        Assert.Equal("Choice key", ExpectedAnswer.ForChoice("key").ToString());
        Assert.Equal("Score 2", ExpectedAnswer.ForScore(2).ToString());
    }

    [Fact]
    public void ForScore_RejectsANegativeLevel()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ExpectedAnswer.ForScore(-1));

    [Fact]
    public void ForScore_AcceptsLevelZero()
        => Assert.Equal(0, ExpectedAnswer.ForScore(0).ScoreLevel);
}
