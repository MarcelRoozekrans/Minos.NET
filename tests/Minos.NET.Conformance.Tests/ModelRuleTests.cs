namespace Minos.Conformance.Tests;

public sealed class ModelRuleTests
{
    [Theory]
    [InlineData("jev-latest", "jev-latest", true)]
    [InlineData("jev-latest", "jev-1.13.0", true)]
    [InlineData("jev-preview", "jev-1.14.0-rc1", true)]
    [InlineData("jev-1.13.0", "jev-1.13.0", true)]
    [InlineData("jev-1.13.0", "jev-1.14.0", false)]
    [InlineData("jev-latest", "jev-preview", false)]
    [InlineData("jev-latest", "gpt-6-luna", false)]
    public void TypeSafesRule(string requested, string reported, bool fits)
        => Assert.Equal(fits, ModelRule.TypeSafe.Matches(requested, reported));

    [Theory]
    [InlineData("jev-latest", "~typesafe/jev-latest", true)]
    [InlineData("jev-latest", "typesafe/jev-1.13-20260917", true)]
    [InlineData("typesafe/jev-latest", "typesafe/jev-1.13-20260917", true)]
    [InlineData("jev-latest", "jev-latest", false)]
    [InlineData("jev-latest", "openai/gpt-6-luna", false)]
    public void OpenRoutersRule(string requested, string reported, bool fits)
        => Assert.Equal(fits, ModelRule.OpenRouter.Matches(requested, reported));

    [Theory]
    [InlineData("openai/gpt-4.1-mini", "openai/gpt-4.1-mini", true)]
    [InlineData("openai/gpt-4.1-mini", "openai/gpt-4.1-mini-2025-04-14", true)]
    [InlineData("openai/gpt-4.1-mini", "gpt-4.1-mini-2025-04-14", true)]
    [InlineData("openai/gpt-4.1-mini", "gpt-4.1-mini", true)]
    [InlineData("gpt-4.1-mini", "openai/gpt-4.1-mini", true)]
    [InlineData("openai/gpt-4.1-mini", "anthropic/gpt-4.1-mini", false)]
    [InlineData("gpt-4.1", "gpt-4.1-mini", false)]
    [InlineData("openai/gpt-4.1", "openai/gpt-4.1-mini", false)]
    [InlineData("openai/gpt-4.1-mini", "openai/gpt-4.1", false)]
    public void TheChatTargetsRule(string requested, string reported, bool fits)
        => Assert.Equal(fits, ChatTargets.ModelRule.Matches(requested, reported));

    [Fact]
    public void ARuleStatesItself_ForTheReport()
    {
        var rule = new ModelRule("Exact.", static (requested, reported) => string.Equals(requested, reported, StringComparison.Ordinal));

        Assert.Equal("Exact.", rule.Description);
        Assert.True(rule.Matches("m", "m"));
        Assert.False(rule.Matches("m", "n"));
    }
}
