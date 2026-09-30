using System.Globalization;
using ZeroAlloc.Jev.Validation;
using ZeroAlloc.Validation;

namespace ZeroAlloc.Jev.Tests;

/// <summary>The ZeroAlloc.Validation spike: the internal question spec, checked by an internal generated validator.</summary>
public sealed class QuestionSpecValidationTests
{
    private static readonly QuestionSpecValidator Validator = new();

    [Fact]
    public void GeneratedValidator_IsInternal() => Assert.False(typeof(QuestionSpecValidator).IsVisible);

    [Fact]
    public void ValidChoice_HasNoFailures() => Assert.True(Validator.Validate(Spec(isChoice: true, "billing", "account")).IsValid);

    [Fact]
    public void Noul_WithoutOptions_HasNoFailures()
        => Assert.True(Validator.Validate(new QuestionSpec { Key = "q", Kind = QuestionKind.Noul, Instructions = "Is it?", Options = [] }).IsValid);

    [Theory]
    [InlineData(true, "JEV001")]
    [InlineData(false, "JEV002")]
    public void NoOptions_IsAnError(bool isChoice, string rule)
    {
        var failure = Only(Validator.Validate(Spec(isChoice)));

        Assert.Equal(rule, failure.ErrorCode);
        Assert.Equal(Severity.Error, failure.Severity);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void ScoreOutsideGuidance_IsAWarning(int levels)
    {
        var failure = Only(Validator.Validate(Spec(isChoice: false, Keys(levels))));

        Assert.Equal("JEV005", failure.ErrorCode);
        Assert.Equal(Severity.Warning, failure.Severity);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public void ScoreWithinGuidance_HasNoFailures(int levels)
        => Assert.True(Validator.Validate(Spec(isChoice: false, Keys(levels))).IsValid);

    [Fact]
    public void ChoiceOver255Options_IsAWarning()
    {
        var failure = Only(Validator.Validate(Spec(isChoice: true, Keys(256))));

        Assert.Equal("JEV005", failure.ErrorCode);
        Assert.Equal(Severity.Warning, failure.Severity);
    }

    [Fact]
    public void ChoiceWith255Options_HasNoFailures() => Assert.True(Validator.Validate(Spec(isChoice: true, Keys(255))).IsValid);

    [Fact]
    public void DuplicateOptionKey_IsOneError_HoweverOftenItRepeats()
    {
        var failure = Only(Validator.Validate(Spec(isChoice: true, "a", "b", "a", "a")));

        Assert.Equal("JEV106", failure.ErrorCode);
        Assert.Equal(Severity.Error, failure.Severity);
        Assert.Contains("'a'", failure.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyOptionKey_IsAnError() => Assert.Equal("JEV106", Only(Validator.Validate(Spec(isChoice: true, "a", ""))).ErrorCode);

    [Fact]
    public void EmptyQuestionKey_IsAnError()
        => Assert.Equal("JEV106", Only(Validator.Validate(Spec(isChoice: true, "a") with { Key = string.Empty })).ErrorCode);

    [Fact]
    public void ValidSpec_AllocatesNothing()
    {
        var spec = Spec(isChoice: true, "billing", "account");

        // Warm up the JIT before measuring.
        _ = Validator.Validate(spec);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            _ = Validator.Validate(spec);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    private static QuestionSpec Spec(bool isChoice, params string[] keys)
        => new()
        {
            Key = "q",
            Kind = isChoice ? QuestionKind.Choice : QuestionKind.Score,
            Instructions = "Which one?",
            Options = [.. keys.Select(key => new OptionSpec(key, key, "Described", -1))],
        };

    private static string[] Keys(int count) => [.. Enumerable.Range(0, count).Select(i => i.ToString(CultureInfo.InvariantCulture))];

    private static ValidationFailure Only(ValidationResult result)
    {
        var failures = result.Failures.ToArray();

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        return Assert.Single(failures);
#pragma warning restore HLQ005
    }
}
