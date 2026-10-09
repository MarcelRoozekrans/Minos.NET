using Minos.Generator;

namespace Minos.Tests;

/// <summary>The library sees the generator's own limits: one linked file, so runtime and compile-time rules agree.</summary>
public sealed class DecisionLimitsTests
{
    [Fact]
    public void Limits_AreTheApisRules()
    {
        Assert.Equal(1, DecisionLimits.MinimumOptions);
        Assert.Equal(2, DecisionLimits.MinimumScoreLevels);
        Assert.Equal(10, DecisionLimits.MaximumScoreLevels);
        Assert.Equal(255, DecisionLimits.MaximumChoiceOptions);
        Assert.Equal(60, DecisionLimits.MaximumJsonDepth);
    }
}
