using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev.Tests;

/// <summary>The library sees the generator's own limits: one linked file, so runtime and compile-time rules agree.</summary>
public sealed class JevLimitsTests
{
    [Fact]
    public void Limits_AreTheApisRules()
    {
        Assert.Equal(1, JevLimits.MinimumOptions);
        Assert.Equal(2, JevLimits.MinimumScoreLevels);
        Assert.Equal(10, JevLimits.MaximumScoreLevels);
        Assert.Equal(255, JevLimits.MaximumChoiceOptions);
        Assert.Equal(60, JevLimits.MaximumJsonDepth);
    }
}
